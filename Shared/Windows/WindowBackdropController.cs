
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using WinRT;

namespace FluentSpotify.Shared.Windows;

public enum BackdropType
{
    None,
    Mica,
    MicaAlt,
    Acrylic,
    AcrylicThin
}

public sealed class WindowBackdropController : IDisposable
{
    private readonly Window _window;
    private ISystemBackdropControllerWithTargets? _currentController;
    private SystemBackdropConfiguration? _configurationSource;
    private FrameworkElement? _themeSource;

    public BackdropType CurrentType { get; private set; } = BackdropType.None;

    public WindowBackdropController(Window window)
    {
        _window = window;
    }

    public bool SetBackdrop(BackdropType type)
    {
        if (type == CurrentType && (type == BackdropType.None || _currentController != null))
        {
            // Тот же материал — только синхронизируем тему, без пересоздания (нет мерцания)
            UpdateThemeConfiguration();
            return true;
        }

        ResetBackdrop();

        if (type == BackdropType.None)
        {
            return true;
        }

        bool isMica = type is BackdropType.Mica or BackdropType.MicaAlt;
        if (isMica ? !MicaController.IsSupported() : !DesktopAcrylicController.IsSupported())
        {
            return false;
        }

        _configurationSource = new SystemBackdropConfiguration { IsInputActive = true };
        UpdateThemeConfiguration();

        var target = _window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>();

        if (isMica)
        {
            var mica = new MicaController
            {
                Kind = type == BackdropType.MicaAlt ? MicaKind.BaseAlt : MicaKind.Base
            };
            mica.AddSystemBackdropTarget(target);
            mica.SetSystemBackdropConfiguration(_configurationSource);
            _currentController = mica;
        }
        else
        {
            var acrylic = new DesktopAcrylicController
            {
                Kind = type == BackdropType.AcrylicThin ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base
            };
            acrylic.AddSystemBackdropTarget(target);
            acrylic.SetSystemBackdropConfiguration(_configurationSource);
            _currentController = acrylic;
        }

        _window.Activated += OnWindowActivated;
        if (_window.Content is FrameworkElement fe)
        {
            _themeSource = fe;
            fe.ActualThemeChanged += OnActualThemeChanged;
        }

        CurrentType = type;
        return true;
    }

    /// <summary>Синхронизирует тему материала с фактической темой содержимого окна.</summary>
    public void UpdateThemeConfiguration()
    {
        if (_configurationSource == null || _window.Content is not FrameworkElement element) return;

        _configurationSource.Theme = element.ActualTheme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default
        };
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => UpdateThemeConfiguration();

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_configurationSource != null)
        {
            _configurationSource.IsInputActive =
                e.WindowActivationState != WindowActivationState.Deactivated;
        }
    }

    private void ResetBackdrop()
    {
        _window.Activated -= OnWindowActivated;
        if (_themeSource != null)
        {
            _themeSource.ActualThemeChanged -= OnActualThemeChanged;
            _themeSource = null;
        }

        if (_currentController != null)
        {
            _currentController.RemoveAllSystemBackdropTargets();
            _currentController.Dispose();
            _currentController = null;
        }

        _configurationSource = null;
        CurrentType = BackdropType.None;
    }

    public void Dispose()
    {
        ResetBackdrop();
    }
}
