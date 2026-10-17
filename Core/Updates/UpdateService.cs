using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentSpotify.Core.Settings;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.Core.Updates;

/// <summary>Найденный на GitHub релиз, который можно установить одной кнопкой.</summary>
public sealed record UpdateInfo(
    AppVersion Version,
    string Tag,
    string Title,
    string Notes,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    bool IsPreRelease,
    string SetupName,
    string SetupUrl,
    long SetupSize,
    string Sha256Url,
    string SignatureUrl);

/// <summary>
/// Обновления через GitHub Releases: проверка при запуске и раз в 6 часов, загрузка
/// установщика (*-Setup-x64.exe) в %LOCALAPPDATA%\FluentSpotify\updates, проверка подписи
/// файла .sha256 ключом владельца (.sha256.sig, см. UpdateSignature), проверка SHA-256
/// установщика и тихий запуск установщика Inno Setup. Релизы без подписи не предлагаются.
/// Методы можно вызывать из любого потока; StateChanged тоже приходит из любого потока.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string Component = "Updates";
    private const string GitHubReleasesUrl = "https://api.github.com/repos/justaprunk/fluent-spotify/releases?per_page=30";

#if DEBUG
    // Только для отладки: подменный список релизов (например, локальный HTTP-сервер)
    private static readonly string ReleasesUrl =
        Environment.GetEnvironmentVariable("FLUENT_SPOTIFY_UPDATE_FEED") is { Length: > 0 } feed ? feed : GitHubReleasesUrl;
#else
    private const string ReleasesUrl = GitHubReleasesUrl;
#endif

    private const string SetupPrefix = "FluentSpotify-";
    private const string SetupSuffix = "-Setup-x64.exe";

    /// <summary>Предел для .sha256 и .sig: это одна строка, большой ответ — явно не тот файл.</summary>
    private const int MaxSmallAssetSize = 4096;

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(20);

    public static string UpdatesDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentSpotify", "updates");

    private readonly SettingsService _settings;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private Timer? _timer;

    public UpdateService(SettingsService settings)
    {
        _settings = settings;

        // Таймаут задаётся на каждый запрос отдельно: загрузка установщика может быть долгой
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub API отклоняет запросы без User-Agent
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FluentSpotify", CurrentVersion.Text));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/justaprunk/fluent-spotify)"));
    }

    public AppVersion CurrentVersion => AppVersion.Current;

    /// <summary>Самая новая доступная версия, новее текущей (в том числе пропущенная).</summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary>Текст последней ошибки проверки или загрузки; null, если всё прошло успешно.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset? LastCheck => _settings.Current.LastUpdateCheck;

    public bool IsChecking { get; private set; }

    public bool IsInstalling { get; private set; }

    public event Action? StateChanged;

    public bool IsSkipped(UpdateInfo update) =>
        string.Equals(_settings.Current.SkippedVersion, update.Version.Text, StringComparison.OrdinalIgnoreCase);

    /// <summary>Отложенная проверка после запуска и периодические проверки в фоне.</summary>
    public void Start()
    {
        _timer ??= new Timer(_ => _ = CheckAsync(), null, StartupDelay, CheckInterval);
    }

    public void Skip(UpdateInfo update)
    {
        AppLog.Write(Component, $"Пропуск версии {update.Version}");
        _settings.Update(s => s.SkippedVersion = update.Version.Text, notify: false);
        RaiseStateChanged();
    }

    /// <summary>Запрашивает список релизов. Ошибки не выбрасываются — они попадают в LastError.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        // Параллельная проверка (таймер + кнопка) ничего не даст — ждём уже идущую
        if (!await _checkLock.WaitAsync(0, cancellationToken))
        {
            await _checkLock.WaitAsync(cancellationToken);
            _checkLock.Release();
            return;
        }

        try
        {
            IsChecking = true;
            RaiseStateChanged();

            bool includePrereleases = _settings.Current.IncludePrereleases;
            Available = await FetchLatestAsync(includePrereleases, cancellationToken);
            LastError = null;
            _settings.Update(s => s.LastUpdateCheck = DateTimeOffset.Now, notify: false);

            AppLog.Write(Component, Available is { } found
                ? $"Доступна версия {found.Version} (текущая {CurrentVersion})"
                : $"Обновлений нет (текущая {CurrentVersion}, бета: {includePrereleases})");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastError = DescribeError(ex);
            AppLog.Write(Component, $"Проверка обновлений не удалась: {LastError}");
            if (ex is not UpdateException) AppLog.Write(Component, ex);
        }
        finally
        {
            IsChecking = false;
            _checkLock.Release();
            RaiseStateChanged();
        }
    }

    private async Task<UpdateInfo?> FetchLatestAsync(bool includePrereleases, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApiTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        EnsureApiSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);

        UpdateInfo? best = null;
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;

            bool preRelease = release.GetProperty("prerelease").GetBoolean();
            if (preRelease && !includePrereleases) continue;

            string tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!AppVersion.TryParse(tag, out var version)) continue;
            if (!(version > CurrentVersion)) continue;
            if (best != null && !(version > best.Version)) continue;

            var info = ToUpdateInfo(release, tag, version, preRelease);
            if (info == null)
            {
                AppLog.Write(Component, $"В релизе {tag} нет *{SetupSuffix}, его .sha256 или подписи .sha256.sig — пропускаем");
                continue;
            }
            best = info;
        }
        return best;
    }

    private static UpdateInfo? ToUpdateInfo(JsonElement release, string tag, AppVersion version, bool preRelease)
    {
        var assets = release.GetProperty("assets").EnumerateArray().ToList();

        static string Name(JsonElement asset) => asset.GetProperty("name").GetString() ?? string.Empty;

        var setup = assets.FirstOrDefault(a => Name(a).EndsWith(SetupSuffix, StringComparison.OrdinalIgnoreCase));
        if (setup.ValueKind != JsonValueKind.Object) return null;

        string setupName = Name(setup);
        var sha = assets.FirstOrDefault(a => string.Equals(Name(a), setupName + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (sha.ValueKind != JsonValueKind.Object) return null;

        // Релизы до 1.3.3 не подписаны — их больше не предлагаем
        var sig = assets.FirstOrDefault(a => string.Equals(Name(a), setupName + ".sha256.sig", StringComparison.OrdinalIgnoreCase));
        if (sig.ValueKind != JsonValueKind.Object) return null;

        // Загружаем и открываем только адреса GitHub по HTTPS — ответ API не должен
        // увести загрузку установщика или кнопку «Подробнее» на чужой адрес или локальный файл
        string setupUrl = setup.GetProperty("browser_download_url").GetString() ?? string.Empty;
        string shaUrl = sha.GetProperty("browser_download_url").GetString() ?? string.Empty;
        string sigUrl = sig.GetProperty("browser_download_url").GetString() ?? string.Empty;
        string htmlUrl = release.GetProperty("html_url").GetString() ?? string.Empty;
        if (!IsTrustedDownloadUrl(setupUrl) || !IsTrustedDownloadUrl(shaUrl) || !IsTrustedDownloadUrl(sigUrl))
        {
            AppLog.Write(Component, $"В релизе {tag} ссылки на файлы ведут не на GitHub — пропускаем");
            return null;
        }
        if (!IsTrustedReleasePage(htmlUrl)) htmlUrl = ReleasesPageUrl;

        string title = release.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name ? name : tag;
        string notes = release.TryGetProperty("body", out var b) ? b.GetString() ?? string.Empty : string.Empty;
        DateTimeOffset? published = release.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetDateTimeOffset()
            : null;

        return new UpdateInfo(
            version,
            tag,
            title,
            notes.Trim(),
            htmlUrl,
            published,
            preRelease,
            setupName,
            setupUrl,
            setup.GetProperty("size").GetInt64(),
            shaUrl,
            sigUrl);
    }

    /// <summary>Страница релизов репозитория — запасной адрес для кнопки «Подробнее».</summary>
    public const string ReleasesPageUrl = "https://github.com/justaprunk/fluent-spotify/releases";

    /// <summary>Файл релиза этого репозитория: https://github.com/justaprunk/fluent-spotify/releases/download/…</summary>
    private static bool IsTrustedDownloadUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && ((uri.Scheme == Uri.UriSchemeHttps
             && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
             && uri.AbsolutePath.StartsWith("/justaprunk/fluent-spotify/releases/download/", StringComparison.OrdinalIgnoreCase))
            || IsDebugFeedUrl(uri));

#if DEBUG
    // Подменный список релизов может отдавать файлы со своего адреса. Подпись при этом
    // проверяется тем же встроенным ключом, так что тестовый релиз нужно подписать им же
    private static bool IsDebugFeedUrl(Uri uri) =>
        ReleasesUrl != GitHubReleasesUrl
        && Uri.TryCreate(ReleasesUrl, UriKind.Absolute, out var feed)
        && Uri.Compare(uri, feed, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
#else
    private static bool IsDebugFeedUrl(Uri uri) => false;
#endif

    /// <summary>Страница релиза этого репозитория на github.com.</summary>
    public static bool IsTrustedReleasePage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith("/justaprunk/fluent-spotify/releases", StringComparison.OrdinalIgnoreCase);

    private static void EnsureApiSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        switch (response.StatusCode)
        {
            case HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests:
            {
                string when = string.Empty;
                if (response.Headers.RetryAfter?.Delta is { } delta)
                {
                    when = " " + Localizer.Format("UpdErr_RetryInMinutes", Math.Max(1, (int)Math.Ceiling(delta.TotalMinutes)));
                }
                else if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
                         && long.TryParse(values.FirstOrDefault(), out long reset))
                {
                    when = " " + Localizer.Format("UpdErr_RetryAfter", Localizer.FormatTime(DateTimeOffset.FromUnixTimeSeconds(reset).ToLocalTime()));
                }

                bool limited = response.StatusCode == HttpStatusCode.TooManyRequests
                               || (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
                                   && remaining.FirstOrDefault() == "0");
                throw new UpdateException(limited
                    ? Localizer.Get("UpdErr_RateLimit") + when
                    : Localizer.Format("UpdErr_Rejected", (int)response.StatusCode) + when);
            }
            case HttpStatusCode.NotFound:
                throw new UpdateException(Localizer.Get("UpdErr_NotFound"));
            default:
                throw new UpdateException(Localizer.Format("UpdErr_Status", (int)response.StatusCode, response.ReasonPhrase));
        }
    }

    /// <summary>
    /// Скачивает установщик, сверяет SHA-256 и запускает его в тихом режиме. Возвращает true,
    /// если установщик запущен — тогда приложение нужно сразу закрыть, чтобы он смог заменить файлы.
    /// При ошибке возвращает false, текст ошибки — в LastError.
    /// </summary>
    public async Task<bool> InstallAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        try
        {
            string setup = await DownloadAsync(update, progress, cancellationToken);
            LaunchInstaller(setup);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex is OperationCanceledException ? Localizer.Get("UpdErr_Cancelled") : DescribeError(ex);
            AppLog.Write(Component, $"Обновление не удалось: {LastError}");
            if (ex is not (UpdateException or OperationCanceledException)) AppLog.Write(Component, ex);
            _lockedInstaller?.Dispose();
            _lockedInstaller = null;
            IsInstalling = false;
            RaiseStateChanged();
            return false;
        }
    }

    /// <summary>
    /// Проверяет подпись .sha256, скачивает установщик, сверяет SHA-256 и возвращает путь к нему.
    /// При неверной подписи или несовпадении контрольной суммы выбрасывается исключение
    /// (скачанный файл удаляется).
    /// </summary>
    private async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        IsInstalling = true;
        LastError = null;
        RaiseStateChanged();

        Directory.CreateDirectory(UpdatesDirectory);
        CleanUpdatesDirectory();

        AppLog.Write(Component, $"Загрузка {update.SetupName} ({update.SetupSize} байт)");
        string expectedHash = await DownloadExpectedHashAsync(update, cancellationToken);

        string target = Path.Combine(UpdatesDirectory, Path.GetFileName(update.SetupName));
        string partial = target + ".partial";

        using (var response = await _http.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? update.SetupSize;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total > 0) progress?.Report(Math.Min(1.0, (double)received / total));
            }
        }

        File.Move(partial, target, overwrite: true);

        // Проверяем и запускаем через один и тот же открытый дескриптор, запрещающий запись и удаление:
        // иначе между проверкой SHA-256 и запуском файл можно было бы подменить
        var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(locked, cancellationToken));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Write(Component, $"SHA-256 не совпадает: ожидалось {expectedHash}, получено {actualHash}");
                throw new UpdateException(Localizer.Get("UpdErr_HashMismatch"));
            }
        }
        catch
        {
            locked.Dispose();
            TryDelete(target);
            throw;
        }

        // Дескриптор держим до выхода из приложения (оно закрывается сразу после запуска установщика)
        _lockedInstaller?.Dispose();
        _lockedInstaller = locked;
        AppLog.Write(Component, $"Установщик загружен и проверен: {target}");
        return target;
    }

    private static FileStream? _lockedInstaller;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLog.Write(Component, $"Не удалось удалить {path}: {ex.Message}");
        }
    }

    private async Task<string> DownloadExpectedHashAsync(UpdateInfo update, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApiTimeout);

        // Подписан сам файл .sha256 — байт в байт, как его записал build-installer.ps1
        byte[] shaFile = await DownloadSmallAssetAsync(update.Sha256Url, timeout.Token);
        string signature = Encoding.ASCII.GetString(await DownloadSmallAssetAsync(update.SignatureUrl, timeout.Token));
        if (!UpdateSignature.Verify(shaFile, signature))
        {
            AppLog.Write(Component, $"Подпись {update.SetupName}.sha256 недействительна");
            throw new UpdateException(Localizer.Get("UpdErr_BadSignature"));
        }

        // Формат как у sha256sum и build-installer.ps1: "<hex> *<имя файла>"
        string[] parts = Encoding.ASCII.GetString(shaFile).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string hash = parts.FirstOrDefault() ?? string.Empty;
        if (parts.Length != 2 || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))
        {
            throw new UpdateException(Localizer.Get("UpdErr_HashCorrupt"));
        }

        // Имя файла с версией тоже подписано: старый подписанный установщик нельзя
        // перевыложить под видом новой версии и откатить пользователей на уязвимую сборку
        string signedName = parts[1].TrimStart('*');
        bool nameMatches = string.Equals(signedName, update.SetupName, StringComparison.OrdinalIgnoreCase)
            && signedName.StartsWith(SetupPrefix, StringComparison.OrdinalIgnoreCase)
            && signedName.EndsWith(SetupSuffix, StringComparison.OrdinalIgnoreCase)
            && AppVersion.TryParse(signedName[SetupPrefix.Length..^SetupSuffix.Length], out var signedVersion)
            && signedVersion.CompareTo(update.Version) == 0;
        if (!nameMatches)
        {
            AppLog.Write(Component, $"Подписанный файл {signedName} не соответствует релизу {update.Tag} ({update.SetupName})");
            throw new UpdateException(Localizer.Get("UpdErr_BadSignature"));
        }
        return hash;
    }

    private async Task<byte[]> DownloadSmallAssetAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxSmallAssetSize) throw new UpdateException(Localizer.Get("UpdErr_BadSignature"));
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Запускает установщик в тихом режиме. После вызова приложение нужно закрыть,
    /// чтобы установщик смог заменить файлы; /RELAUNCH=1 просит его снова запустить приложение.
    /// </summary>
    private static void LaunchInstaller(string setupPath)
    {
        string log = Path.Combine(UpdatesDirectory, "install.log");
        var info = new ProcessStartInfo(setupPath)
        {
            Arguments = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1 \"/LOG={log}\"",
            UseShellExecute = true,
            WorkingDirectory = UpdatesDirectory
        };

        AppLog.Write(Component, $"Запуск установщика: {setupPath} {info.Arguments}");
        Process.Start(info);
    }

    private static void CleanUpdatesDirectory()
    {
        foreach (string path in Directory.EnumerateFiles(UpdatesDirectory))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                AppLog.Write(Component, $"Не удалось удалить {path}: {ex.Message}");
            }
        }
    }

    private static string DescribeError(Exception ex) => ex switch
    {
        UpdateException => ex.Message,
        HttpRequestException { StatusCode: { } code } => Localizer.Format("UpdErr_Download", (int)code, code),
        HttpRequestException => Localizer.Get("UpdErr_NoInternet"),
        OperationCanceledException => Localizer.Get("UpdErr_Timeout"),
        JsonException or KeyNotFoundException or InvalidOperationException => Localizer.Get("UpdErr_BadResponse"),
        IOException or UnauthorizedAccessException => Localizer.Format("UpdErr_SaveFile", ex.Message),
        _ => ex.Message
    };

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Write(Component, ex);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _http.Dispose();
    }
}

/// <summary>Ошибка обновления с понятным пользователю текстом.</summary>
public sealed class UpdateException(string message) : Exception(message);
