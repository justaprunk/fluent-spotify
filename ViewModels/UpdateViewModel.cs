using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentSpotify.Core.Settings;
using FluentSpotify.Core.Updates;
using FluentSpotify.Shared.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentSpotify.ViewModels;

/// <summary>
/// Состояние обновлений для InfoBar главного окна и раздела «Обновления» в настройках.
/// Одна копия на приложение: создаётся в UI-потоке вместе с главным окном.
/// </summary>
public partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _updates;
    private readonly SettingsService _settings;
    private readonly DispatcherQueue _dispatcher;
    private string? _dismissedVersion;


    public UpdateViewModel(UpdateService updates, SettingsService settings)
    {
        _updates = updates;
        _settings = settings;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _includePrereleases = settings.Current.IncludePrereleases;

        // Сервис сообщает об изменениях из фоновых потоков
        _updates.StateChanged += () => _dispatcher.TryEnqueue(Refresh);
    }

    /// <summary>«Подробнее» в InfoBar: окно открывает настройки с описанием версии.</summary>
    public event Action? DetailsRequested;

    private UpdateInfo? Available => _updates.Available;

    public string CurrentVersionText => Localizer.Format("About_Version", _updates.CurrentVersion);

    public string LastCheckText => _updates.LastCheck is { } time
        ? Localizer.Format("Update_LastCheck", FormatTime(time))
        : Localizer.Get("Update_NeverChecked");

    /// <summary>Подпись карточки «О приложении»: версия и время последней проверки.</summary>
    public string VersionSummaryText => $"{CurrentVersionText} · {LastCheckText}";

    public bool IsChecking => _updates.IsChecking;

    public bool IsInstalling => _updates.IsInstalling;

    public bool IsBusy => IsChecking || IsInstalling;

    public bool CanCheck => !IsBusy;

    public bool IsUpdateAvailable => Available != null;

    public bool CanInstall => IsUpdateAvailable && !IsBusy;

    public bool IsAvailableSkipped => Available is { } a && _updates.IsSkipped(a);

    public bool CanSkip => IsUpdateAvailable && !IsAvailableSkipped && !IsBusy;

    public string AvailableTitle => Available is { } a
        ? Localizer.Format(a.IsPreRelease ? "Update_AvailableBeta" : "Update_Available", a.Version)
        : string.Empty;

    public string AvailableDetails => Available is { } a
        ? string.Join(" · ", new[]
            {
                a.Title != a.Tag ? a.Title : null,
                a.PublishedAt is { } p ? Localizer.Format("Update_Published", Localizer.FormatDate(p.ToLocalTime())) : null,
                Localizer.Format("Update_SizeMb", a.SetupSize / 1024.0 / 1024.0),
                IsAvailableSkipped ? Localizer.Get("Update_Skipped") : null
            }.Where(s => s != null))
        : string.Empty;

    public string ReleaseNotes => Available?.Notes is { Length: > 0 } notes ? PlainText(notes) : Localizer.Get("Update_NoNotes");

    /// <summary>Описание релиза пишется в Markdown — убираем разметку, которая мешает читать простой текст.</summary>
    private static string PlainText(string markdown) => string.Join('\n', markdown
        .Replace("\r\n", "\n")
        .Split('\n')
        .Select(line =>
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) return trimmed.TrimStart('#').Trim();
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ")) return "• " + trimmed[2..];
            return line;
        })
        .Select(line => line.Replace("**", string.Empty).Replace("`", string.Empty)));

    /// <summary>Строка состояния под кнопкой «Проверить обновления».</summary>
    public string StatusText =>
        IsChecking ? Localizer.Get("Update_Checking")
        : IsInstalling ? Localizer.Format("Update_Downloading", DownloadProgress)
        : _updates.LastError is { } error ? error
        : IsUpdateAvailable ? AvailableTitle
        : _updates.LastCheck != null ? Localizer.Get("Update_UpToDate")
        : string.Empty;

    public bool HasError => _updates.LastError != null && !IsBusy;

    public string? ErrorText => _updates.LastError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(InfoBarMessage))]
    private double _downloadProgress;

    /// <summary>
    /// InfoBar показывается для новой непропущенной версии, пока его не закрыли,
    /// и всё время загрузки. Привязка двусторонняя: крестик InfoBar записывает сюда false.
    /// </summary>
    public bool IsInfoBarOpen
    {
        get => IsInstalling || (Available is { } a && !_updates.IsSkipped(a) && _dismissedVersion != a.Version.Text);
        set
        {
            if (value || IsInstalling) return;
            _dismissedVersion = Available?.Version.Text;
            OnPropertyChanged();
        }
    }

    public string InfoBarMessage =>
        IsInstalling ? Localizer.Format("Update_DownloadingRestart", DownloadProgress)
        : _installError is { } error ? Localizer.Format("Update_Failed", error)
        : Localizer.Get("Update_Ready");

    public InfoBarSeverity InfoBarSeverity => !IsInstalling && _installError != null
        ? InfoBarSeverity.Error
        : InfoBarSeverity.Informational;

    /// <summary>Ошибка последней попытки обновления (ошибки фоновых проверок в InfoBar не показываем).</summary>
    private string? _installError;

    /// <summary>«Подробнее» нажато: открывшаяся страница настроек один раз прокрутит к описанию версии.</summary>
    private bool _scrollToDetailsPending;

    public bool TakeScrollToDetails()
    {
        bool pending = _scrollToDetailsPending;
        _scrollToDetailsPending = false;
        return pending;
    }

    [ObservableProperty]
    private bool _includePrereleases;

    partial void OnIncludePrereleasesChanged(bool value)
    {
        if (_settings.Current.IncludePrereleases == value) return;
        _settings.Update(s => s.IncludePrereleases = value, notify: false);
        _ = _updates.CheckAsync();
    }

    [RelayCommand]
    private Task CheckAsync() => _updates.CheckAsync();

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (Available is not { } update || IsBusy) return;

        DownloadProgress = 0;
        _installError = null;
        // Progress<T> создан в UI-потоке — отчёты приходят сюда же
        var progress = new Progress<double>(value =>
        {
            double percent = Math.Floor(value * 100);
            if (percent != DownloadProgress) DownloadProgress = percent;
        });

        if (await _updates.InstallAsync(update, progress))
        {
            // Установщик уже запущен: закрываемся, чтобы он смог заменить файлы
            AppLog.Write("Updates", "Выход для установки обновления");
            Application.Current.Exit();
            return;
        }

        _installError = _updates.LastError ?? Localizer.Get("Update_UnknownError");
        Refresh();
    }

    [RelayCommand]
    private void Skip()
    {
        if (Available is { } update) _updates.Skip(update);
    }

    [RelayCommand]
    private void ShowDetails()
    {
        IsDetailsExpanded = true;
        _scrollToDetailsPending = true;
        DetailsRequested?.Invoke();
    }

    /// <summary>Раскрыт ли блок «Что нового» в настройках.</summary>
    [ObservableProperty]
    private bool _isDetailsExpanded;

    [RelayCommand]
    private void OpenReleasePage()
    {
        // Через оболочку открываем только страницу релиза на github.com, никогда не файл или чужой адрес
        string url = Available?.HtmlUrl is { } u && UpdateService.IsTrustedReleasePage(u) ? u : UpdateService.ReleasesPageUrl;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write("Updates", ex);
        }
    }

    private void Refresh()
    {
        // Пустое имя свойства — обновить все привязки к модели
        OnPropertyChanged(string.Empty);
    }

    private static string FormatTime(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        return local.Date == DateTime.Today ? Localizer.Format("Time_TodayAt", Localizer.FormatTime(local))
            : local.Date == DateTime.Today.AddDays(-1) ? Localizer.Format("Time_YesterdayAt", Localizer.FormatTime(local))
            : Localizer.Format("Format_DateTime", Localizer.FormatDate(local), Localizer.FormatTime(local));
    }
}
