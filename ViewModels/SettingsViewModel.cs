using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentSpotify.Core.Playback;
using FluentSpotify.Core.Settings;
using Microsoft.UI.Composition.SystemBackdrops;

namespace FluentSpotify.ViewModels;

public sealed record SettingsOption(string Value, string Title, string? Description = null);

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly MainViewModel _main;
    private bool _initializing = true;

    public IReadOnlyList<SettingsOption> BackdropOptions { get; } =
    [
        new("Mica", "Mica", "Мягко окрашивается цветом обоев. Рекомендуется для основного окна."),
        new("MicaAlt", "Mica Alt", "Более насыщенный вариант Mica — сильнее заметен цвет обоев."),
        new("Acrylic", "Acrylic", "Полупрозрачное размытие того, что находится позади окна."),
        new("AcrylicThin", "Acrylic Thin", "Более прозрачный и светлый вариант Acrylic."),
        new("None", "Без материала", "Сплошной фон без эффектов.")
    ];

    public IReadOnlyList<SettingsOption> ThemeOptions { get; } =
    [
        new("System", "Как в Windows"),
        new("Light", "Светлая"),
        new("Dark", "Тёмная")
    ];

    [ObservableProperty]
    private SettingsOption? _selectedBackdrop;

    [ObservableProperty]
    private SettingsOption? _selectedTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOwnThemeEnabled))]
    private bool _useShellTheme;

    public bool IsOwnThemeEnabled => !UseShellTheme;

    [ObservableProperty]
    private bool _launchClientInBackground;

    /// <summary>Какой официальный клиент найден на этом ПК.</summary>
    public string ClientStatusText { get; } = SpotifyClientLauncher.DetectInstall().Kind switch
    {
        SpotifyInstallKind.Desktop => "Найден клиент Spotify (установщик с сайта).",
        SpotifyInstallKind.MicrosoftStore => "Найден клиент Spotify из Microsoft Store.",
        _ => "Официальный клиент Spotify не найден — установите его из Microsoft Store или с spotify.com."
    };

    public bool IsClientMissing { get; } = SpotifyClientLauncher.DetectInstall().Kind == SpotifyInstallKind.NotInstalled;

    public bool IsMicaSupported { get; } = MicaController.IsSupported();
    public bool IsAcrylicSupported { get; } = DesktopAcrylicController.IsSupported();

    public string SupportNote =>
        (IsMicaSupported, IsAcrylicSupported) switch
        {
            (true, true) => string.Empty,
            (false, true) => "Mica доступна только в Windows 11 — вместо неё будет сплошной фон.",
            (true, false) => "Acrylic недоступен в этой системе — вместо него будет сплошной фон.",
            _ => "Системные материалы недоступны в этой системе — будет использоваться сплошной фон."
        };

    public bool HasSupportNote => SupportNote.Length > 0;

    public SettingsViewModel(SettingsService settings, MainViewModel main)
    {
        _settings = settings;
        _main = main;

        var current = settings.Current;
        SelectedBackdrop = BackdropOptions.FirstOrDefault(o => o.Value == current.Backdrop) ?? BackdropOptions[0];
        SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == current.Theme) ?? ThemeOptions[0];
        UseShellTheme = current.UseShellTheme;
        LaunchClientInBackground = current.LaunchClientInBackground;
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
    private void ChangeClientId()
    {
        // Выход из аккаунта и возврат на экран входа, где можно ввести другой Client ID
        _main.LogoutCommand.Execute(null);
        _main.EditClientIdCommand.Execute(null);
    }

    [RelayCommand]
    private void Logout() => _main.LogoutCommand.Execute(null);
}
