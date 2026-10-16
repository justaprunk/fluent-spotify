
using Microsoft.UI.Xaml;

namespace FluentSpotify.Shared.Windows;

/// <summary>
/// Базовое окно с поддержкой System Backdrops (Mica/Acrylic) и темы.
/// По умолчанию следует теме шелла (theme.json, применяется на лету).
/// Приложение со своими настройками выставляет FollowShellTheme = false и вызывает ApplyTheme само.
/// Наследник должен вызвать <see cref="ApplyCurrentTheme"/> после InitializeComponent()
/// (иначе тема шелла применится при первой активации окна).
/// </summary>
public class FluentWindow : Window
{
    private readonly WindowBackdropController _backdropController;
    private readonly Action<ShellThemeConfig> _themeChangedHandler;
    private bool _followShellTheme = true;
    private bool _subscribed;
    private bool _themeApplied;

    protected WindowBackdropController BackdropController => _backdropController;

    public FluentWindow()
    {
        _backdropController = new WindowBackdropController(this);

        // Делегат сохраняем, чтобы отписаться при закрытии — иначе синглтон держит окно в памяти
        _themeChangedHandler = theme => DispatcherQueue.TryEnqueue(() =>
        {
            if (_followShellTheme) ApplyTheme(theme);
        });

        Activated += OnFirstActivated;
        Closed += (s, e) =>
        {
            Unsubscribe();
            _backdropController.Dispose();
        };
    }

    /// <summary>
    /// Следовать теме шелла (~/.config/wshm/theme.json). ThemeManager создаётся только
    /// когда это свойство включено, поэтому обычные приложения не трогают файлы шелла.
    /// </summary>
    public bool FollowShellTheme
    {
        get => _followShellTheme;
        set
        {
            if (_followShellTheme == value) return;
            _followShellTheme = value;
            if (value)
            {
                ApplyCurrentTheme();
            }
            else
            {
                Unsubscribe();
            }
        }
    }

    /// <summary>Применить текущую тему шелла немедленно (после InitializeComponent).</summary>
    protected void ApplyCurrentTheme()
    {
        if (!_followShellTheme) return;
        Subscribe();
        ApplyTheme(ThemeManager.Instance.CurrentTheme);
    }

    /// <summary>Применить произвольную конфигурацию темы (материал + светлая/тёмная).</summary>
    public void ApplyTheme(ShellThemeConfig theme)
    {
        _themeApplied = true;
        OnApplyTheme(theme);
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        ThemeManager.Instance.ThemeChanged += _themeChangedHandler;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        ThemeManager.Instance.ThemeChanged -= _themeChangedHandler;
        _subscribed = false;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        if (!_themeApplied)
        {
            ApplyCurrentTheme();
        }
    }

    protected virtual void OnApplyTheme(ShellThemeConfig theme)
    {
        // Переключение темы палитры XAML (Dark / Light / System)
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme.Theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        // Переключение материала DWM
        var backdropType = theme.Backdrop switch
        {
            "Mica" => BackdropType.Mica,
            "MicaAlt" => BackdropType.MicaAlt,
            "Acrylic" => BackdropType.Acrylic,
            "AcrylicThin" => BackdropType.AcrylicThin,
            _ => BackdropType.None
        };
        _backdropController.SetBackdrop(backdropType);
    }
}
