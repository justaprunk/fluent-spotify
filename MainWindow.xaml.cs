using System.Collections.Specialized;
using System.ComponentModel;
using FluentSpotify.Core;
using FluentSpotify.Core.Api;
using FluentSpotify.Core.Settings;
using FluentSpotify.ViewModels;
using FluentSpotify.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpotifyAPI.Web;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using FluentSpotify.Shared.Windows;

namespace FluentSpotify;

public sealed partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    /// <summary>InfoBar о новой версии.</summary>
    public UpdateViewModel Updates { get; }

    private readonly SettingsService _settings;
    private readonly AppWindow _appWindow;
    private readonly List<NavigationViewItem> _playlistItems = new();
    private readonly WindowMinSize _minSize;

    public MainWindow(MainViewModel viewModel, SettingsService settings, UpdateViewModel updates)
    {
        // ViewModel должен быть задан до InitializeComponent — на него ссылаются x:Bind
        ViewModel = viewModel;
        Updates = updates;
        _settings = settings;
        InitializeComponent();

        // Язык разметки: по нему выбираются шрифты для иероглифов (японские, китайские, корейские начертания)
        RootGrid.Language = Localizer.CurrentLanguage;

        // Привязка HWND к AppWindow для кастомизации заголовка
        var hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.Resize(new SizeInt32(
            NativeWindowHelper.DipToPixels(hwnd, 1200),
            NativeWindowHelper.DipToPixels(hwnd, 800)));

        SetupCustomTitleBar();

        // Меньше этого размера вёрстка плеера и списков теряет смысл
        _minSize = WindowMinSize.Apply(hwnd, 520, 560);

        // Иконка окна: панель задач, Alt+Tab (иконку exe задаёт ApplicationIcon в csproj)
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        // Материал и тема: собственные настройки приложения или тема оболочки WSHM
        ApplyAppearance(_settings.Current);
        _settings.Changed += s => DispatcherQueue.TryEnqueue(() => ApplyAppearance(s));
        RootGrid.ActualThemeChanged += (s, e) => UpdateTitleBarColors();

        ViewModel.Playlists.CollectionChanged += OnPlaylistsChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Updates.DetailsRequested += OnUpdateDetailsRequested;

        // У Window в WinUI 3 нет события Loaded — используем корневой элемент
        RootGrid.Loaded += OnRootLoaded;
    }

    private void ApplyAppearance(AppSettings settings)
    {
        FollowShellTheme = settings.UseShellTheme;
        if (!settings.UseShellTheme)
        {
            ApplyTheme(new ShellThemeConfig { Backdrop = settings.Backdrop, Theme = settings.Theme });
        }
    }

    protected override void OnApplyTheme(ShellThemeConfig theme)
    {
        base.OnApplyTheme(theme);

        // Если материал выключен или не поддерживается (Windows 10) — показываем сплошной фон
        SolidBackground.Visibility = BackdropController.CurrentType == BackdropType.None
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateTitleBarColors();
    }

    private void SetupCustomTitleBar()
    {
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;

            // Назначаем кастомный Drag-элемент заголовка окна
            SetTitleBar(AppTitleBar);

            // Делаем кнопки свернуть/развернуть/закрыть прозрачными для бесшовного фона
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    /// <summary>Системные кнопки заголовка не следуют теме XAML — красим их вручную.</summary>
    private void UpdateTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var titleBar = _appWindow.TitleBar;
        var foreground = dark ? Colors.White : Colors.Black;

        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A) : Color.FromArgb(0xFF, 0x70, 0x70, 0x70);
        titleBar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00);
        titleBar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x28, 0x00, 0x00, 0x00);
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnRootLoaded;
        await ViewModel.InitializeAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsAuthenticated)) return;

        if (ViewModel.IsAuthenticated)
        {
            // Стартовый переход на главную страницу (SelectionChanged выполнит навигацию)
            NavView.SelectedItem = NavView.MenuItems[0];
        }
        else
        {
            NavView.SelectedItem = null;
            ContentFrame.Content = null;
            ContentFrame.BackStack.Clear();
        }
    }

    private void OnPlaylistsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in _playlistItems)
        {
            NavView.MenuItems.Remove(item);
        }
        _playlistItems.Clear();

        foreach (var playlist in ViewModel.Playlists)
        {
            var item = new NavigationViewItem
            {
                Content = playlist.Name,
                Tag = playlist,
                Icon = new FontIcon { Glyph = "" }
            };
            _playlistItems.Add(item);
            NavView.MenuItems.Add(item);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem navItem) return;

        switch (navItem.Tag)
        {
            case "Settings":
                ContentFrame.Navigate(typeof(SettingsView));
                break;
            case "Home":
                ContentFrame.Navigate(typeof(HomeView));
                break;
            case "Search":
                ContentFrame.Navigate(typeof(SearchView));
                break;
            case "Liked":
                ContentFrame.Navigate(typeof(PlaylistDetailView), SpotifyApiClient.LikedSongsId);
                break;
            case "Wrapped":
                ContentFrame.Navigate(typeof(WrappedView));
                break;
            case FullPlaylist { Id: { Length: > 0 } playlistId }:
                // Переход на детальную страницу выбранного плейлиста
                ContentFrame.Navigate(typeof(PlaylistDetailView), playlistId);
                break;
        }
    }

    private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        // В режиме Minimal кнопка-гамбургер висит поверх содержимого — освобождаем под неё место
        bool minimal = args.DisplayMode == NavigationViewDisplayMode.Minimal;
        ContentFrame.Margin = new Thickness(0, minimal ? 40 : 0, 12, 0);
    }

    /// <summary>«Подробнее» в InfoBar: описание версии в настройках, а до входа — страница релиза.</summary>
    private void OnUpdateDetailsRequested()
    {
        var settingsItem = NavView.FooterMenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(i => (i.Tag as string) == "Settings");

        if (ViewModel.IsAuthenticated && settingsItem != null)
        {
            NavView.SelectedItem = settingsItem;
        }
        else
        {
            Updates.OpenReleasePageCommand.Execute(null);
        }
    }

    private void LogoutItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ViewModel.LogoutCommand.Execute(null);
    }
}
