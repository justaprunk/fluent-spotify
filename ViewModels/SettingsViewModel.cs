using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentSpotify.Core.Playback;
using FluentSpotify.Core.Settings;
using FluentSpotify.Shared.Diagnostics;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;

namespace FluentSpotify.ViewModels;

public sealed record SettingsOption(string Value, string Title, string? Description = null);

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly MainViewModel _main;
    private bool _initializing = true;

    public IReadOnlyList<SettingsOption> BackdropOptions { get; } =
    [
        new("Mica", "Mica", Localizer.Get("Backdrop_Mica_Desc")),
        new("MicaAlt", "Mica Alt", Localizer.Get("Backdrop_MicaAlt_Desc")),
        new("Acrylic", "Acrylic", Localizer.Get("Backdrop_Acrylic_Desc")),
        new("AcrylicThin", "Acrylic Thin", Localizer.Get("Backdrop_AcrylicThin_Desc")),
        new("None", Localizer.Get("Backdrop_None"), Localizer.Get("Backdrop_None_Desc"))
    ];

    public IReadOnlyList<SettingsOption> ThemeOptions { get; } =
    [
        new("System", Localizer.Get("Theme_System")),
        new("Light", Localizer.Get("Theme_Light")),
        new("Dark", Localizer.Get("Theme_Dark"))
    ];

    /// <summary>«Как в системе» (пустое значение) и языки, каждый — названием на самом языке.</summary>
    public IReadOnlyList<SettingsOption> LanguageOptions { get; } =
    [
        new(string.Empty, Localizer.Get("Settings_LanguageSystem")),
        .. Localizer.Languages.Select(l => new SettingsOption(l.Code, l.NativeName))
    ];

    [ObservableProperty]
    private SettingsOption? _selectedBackdrop;

    [ObservableProperty]
    private SettingsOption? _selectedTheme;

    [ObservableProperty]
    private SettingsOption? _selectedLanguage;

    /// <summary>Язык в настройках отличается от того, с которым запущено приложение.</summary>
    [ObservableProperty]
    private bool _isRestartRequired;

    [ObservableProperty]
    private string _restartTitle = string.Empty;

    [ObservableProperty]
    private string _restartMessage = string.Empty;

    [ObservableProperty]
    private string _restartButtonText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOwnThemeEnabled))]
    private bool _useShellTheme;

    public bool IsOwnThemeEnabled => !UseShellTheme;

    [ObservableProperty]
    private bool _launchClientInBackground;

    /// <summary>Какой официальный клиент найден на этом ПК.</summary>
    public string ClientStatusText { get; } = SpotifyClientLauncher.DetectInstall().Kind switch
    {
        SpotifyInstallKind.Desktop => Localizer.Get("Client_Desktop"),
        SpotifyInstallKind.MicrosoftStore => Localizer.Get("Client_Store"),
        _ => Localizer.Get("Client_Missing")
    };

    public bool IsClientMissing { get; } = SpotifyClientLauncher.DetectInstall().Kind == SpotifyInstallKind.NotInstalled;

    public bool IsMicaSupported { get; } = MicaController.IsSupported();
    public bool IsAcrylicSupported { get; } = DesktopAcrylicController.IsSupported();

    public string SupportNote =>
        (IsMicaSupported, IsAcrylicSupported) switch
        {
            (true, true) => string.Empty,
            (false, true) => Localizer.Get("Support_NoMica"),
            (true, false) => Localizer.Get("Support_NoAcrylic"),
            _ => Localizer.Get("Support_NoBackdrops")
        };

    public bool HasSupportNote => SupportNote.Length > 0;

    /// <summary>Раздел «О приложении»: версия и обновления, общее состояние с InfoBar главного окна.</summary>
    public UpdateViewModel Updates { get; }

    public SettingsViewModel(SettingsService settings, MainViewModel main, UpdateViewModel updates)
    {
        _settings = settings;
        _main = main;
        Updates = updates;

        var current = settings.Current;
        SelectedBackdrop = BackdropOptions.FirstOrDefault(o => o.Value == current.Backdrop) ?? BackdropOptions[0];
        SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == current.Theme) ?? ThemeOptions[0];
        SelectedLanguage = LanguageOptions.FirstOrDefault(o => o.Value == (current.Language ?? string.Empty)) ?? LanguageOptions[0];
        UseShellTheme = current.UseShellTheme;
        LaunchClientInBackground = current.LaunchClientInBackground;
        UpdateRestartState();
        _initializing = false;
    }

    partial void OnSelectedBackdropChanged(SettingsOption? value)
    {
        if (_initializing || value == null) return;
        _settings.Update(s => s.Backdrop = value.Value);
    }

    partial void OnSelectedThemeChanged(SettingsOption? value)
    {
        if (_initializing || value == null) return;
        _settings.Update(s => s.Theme = value.Value);
    }

    partial void OnSelectedLanguageChanged(SettingsOption? value)
    {
        if (_initializing || value == null) return;
        _settings.Update(s => s.Language = value.Value.Length > 0 ? value.Value : null, notify: false);
        UpdateRestartState();
    }

    /// <summary>
    /// Строки берутся при создании окна и страниц, поэтому новый язык включается после перезапуска.
    /// Подсказку показываем на выбранном языке — её поймёт тот, кто его выбрал.
    /// </summary>
    private void UpdateRestartState()
    {
        string target = Localizer.Resolve(_settings.Current.Language);
        IsRestartRequired = target != Localizer.Resolve(Localizer.StartupSetting);
        RestartTitle = Localizer.GetIn(target, "Settings_RestartTitle");
        RestartMessage = Localizer.GetIn(target, "Settings_RestartMessage");
        RestartButtonText = Localizer.GetIn(target, "Settings_Restart");
    }

    partial void OnUseShellThemeChanged(bool value)
    {
        if (_initializing) return;
        _settings.Update(s => s.UseShellTheme = value);
    }

    partial void OnLaunchClientInBackgroundChanged(bool value)
    {
        if (_initializing) return;
        _settings.Update(s => s.LaunchClientInBackground = value);
    }

    [RelayCommand]
    private void Restart()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Settings", ex);
            return;
        }
        Application.Current.Exit();
    }

    [RelayCommand]
    private void ChangeClientId()
    {
        // Выход из аккаунта и возврат на экран входа, где можно ввести другой Client ID
        _main.LogoutCommand.Execute(null);
        _main.EditClientIdCommand.Execute(null);
    }

    [RelayCommand]
    private void Logout() => _main.LogoutCommand.Execute(null);
}
