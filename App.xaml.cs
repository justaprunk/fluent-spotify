using FluentSpotify.Core.Api;
using FluentSpotify.Core.Auth;
using FluentSpotify.Core.Library;
using FluentSpotify.Core.NowPlaying;
using FluentSpotify.Core.Playback;
using FluentSpotify.Core.Settings;
using FluentSpotify.Core.Updates;
using FluentSpotify.Localization;
using FluentSpotify.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify;

public partial class App : Application
{
    private Window? _mainWindow;

    public static IServiceProvider Services { get; private set; } = null!;

    // Client ID из Spotify Developer Dashboard: задайте здесь или в переменной окружения
    // FLUENT_SPOTIFY_CLIENT_ID. Redirect URI в Dashboard: http://127.0.0.1:5000/callback/
    private const string DefaultSpotifyClientId = "YOUR_SPOTIFY_CLIENT_ID";

    public App()
    {
        InitializeComponent();

        UnhandledException += (sender, e) =>
        {
            // Не роняем приложение из-за временных сбоев API, но обязательно логируем
            AppLog.Write("FluentSpotify", e.Exception);
            e.Handled = true;
        };

        // Исключения фоновых потоков и забытых задач XAML не видит — пусть хотя бы остаются в логе
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            AppLog.Write("FluentSpotify", $"Unhandled (terminating: {e.IsTerminating}): {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            AppLog.Write("FluentSpotify", e.Exception);
            e.SetObserved();
        };

        ConfigureServices();
    }

    private static void ConfigureServices()
    {
        var services = new ServiceCollection();
        var settings = new SettingsService();

        // Язык интерфейса выбирается до создания окна и view-моделей: строки берутся при их создании.
        // FLUENT_SPOTIFY_LANGUAGE (например, de-DE) перекрывает настройку — для проверки переводов.
        Localizer.StartupSetting = settings.Current.Language;
        Localizer.Initialize(Environment.GetEnvironmentVariable("FLUENT_SPOTIFY_LANGUAGE") is { Length: > 0 } language
            ? language
            : settings.Current.Language);

        // Приоритет: переменная окружения → сохранённые настройки (ввод на экране входа) → константа
        string clientId =
            Environment.GetEnvironmentVariable("FLUENT_SPOTIFY_CLIENT_ID") is { Length: > 0 } fromEnv ? fromEnv
            : settings.Current.ClientId is { Length: > 0 } fromSettings ? fromSettings
            : DefaultSpotifyClientId;

        // 1. Core Services
        services.AddSingleton(settings);
        services.AddSingleton(new PkceAuthService(clientId));
        services.AddSingleton<SpotifyApiClient>();
        services.AddSingleton<SmtcBridge>();
        services.AddSingleton<SpotifyClientLauncher>();
        services.AddSingleton<BackgroundPlaybackService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<PlaylistFolderService>();
        services.AddSingleton<LyricsService>();
        services.AddSingleton<ArtistBioService>();

        // 2. ViewModels
        services.AddSingleton<NowPlayingViewModel>();
        services.AddSingleton<PlayerBarViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<UpdateViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<PlaylistViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<WrappedViewModel>();

        // 3. Main Window
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Окно и view-модели создаются в UI-потоке (им нужен DispatcherQueue)
        _mainWindow = Services.GetRequiredService<MainWindow>();
        _mainWindow.Activate();

        // Мост системных медиаклавиш Windows
        try
        {
            _ = Services.GetRequiredService<SmtcBridge>();
        }
        catch (Exception ex)
        {
            AppLog.Write("FluentSpotify", ex);
        }

        // Проверка обновлений: отложенно после запуска и затем раз в 6 часов
        Services.GetRequiredService<UpdateService>().Start();
    }
}

#region XAML Value Converters

public sealed class PlayPauseIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Segoe Fluent Icons, заполненные варианты: F5B0 = PlaySolid, F8AE = PauseSolid
        return value is true ? "" : "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// URL (string) или список изображений Spotify → ImageSource. Пустые значения дают null.
/// </summary>
public sealed class UrlToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        string? url = value switch
        {
            string s => s,
            IEnumerable<SpotifyAPI.Web.Image> images => images.FirstOrDefault()?.Url,
            _ => null
        };

        return !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? new BitmapImage(uri)
            : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

#endregion
