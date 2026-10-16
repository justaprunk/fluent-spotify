using System.Text.Json;
using System.Text.RegularExpressions;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.Core.Settings;

/// <summary>
/// Пользовательские настройки: %LOCALAPPDATA%\FluentSpotify\settings.json
/// </summary>
public sealed partial class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FluentSpotify",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Client ID приложения из Spotify Developer Dashboard. Это не секрет:
    /// при PKCE client secret не используется.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>Материал окна: None, Mica, MicaAlt, Acrylic, AcrylicThin.</summary>
    public string Backdrop { get; set; } = "Mica";

    /// <summary>Тема: System, Light, Dark.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Язык интерфейса (код из Localizer.Languages); пусто — как в Windows.</summary>
    public string? Language { get; set; }

    /// <summary>Брать материал и тему из оболочки WSHM (~/.config/wshm/theme.json).</summary>
    public bool UseShellTheme { get; set; }

    /// <summary>
    /// Автоматически запускать официальный клиент Spotify скрытым — он служит устройством
    /// воспроизведения (Spotify Connect). В этом режиме системные медиаэлементы Windows
    /// показывает сам клиент, наш SmtcBridge выключен, чтобы не было двух карточек.
    /// </summary>
    public bool LaunchClientInBackground { get; set; } = true;

    /// <summary>На главной показывать плейлисты по папкам — отдельный раздел на каждую папку.</summary>
    public bool GroupHomeByFolders { get; set; } = true;

    /// <summary>Предлагать пре-релизы (бета-версии) из GitHub Releases.</summary>
    public bool IncludePrereleases { get; set; } = true;

    /// <summary>Версия, которую пользователь попросил пропустить (без префикса v).</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>Время последней успешной проверки обновлений.</summary>
    public DateTimeOffset? LastUpdateCheck { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("AppSettings", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Client ID Spotify — 32 шестнадцатеричных символа.</summary>
    public static bool IsValidClientId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ClientIdRegex().IsMatch(value.Trim());

    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex ClientIdRegex();
}

/// <summary>
/// Единая точка доступа к настройкам: хранит текущие значения, сохраняет на диск
/// и уведомляет подписчиков (окно применяет материал и тему на лету).
/// </summary>
public sealed class SettingsService
{
    public AppSettings Current { get; }

    public event Action<AppSettings>? Changed;

    public SettingsService()
    {
        Current = AppSettings.Load();
    }

    /// <param name="notify">
    /// false — только сохранить, без события Changed (служебные значения вроде времени
    /// проверки обновлений, из-за которых не нужно заново применять оформление окна).
    /// </param>
    public void Update(Action<AppSettings> change, bool notify = true)
    {
        change(Current);
        try
        {
            Current.Save();
        }
        catch (Exception ex)
        {
            AppLog.Write("SettingsService", ex);
        }
        if (notify)
        {
            Changed?.Invoke(Current);
        }
    }
}
