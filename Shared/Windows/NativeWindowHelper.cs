using System.Runtime.InteropServices;

namespace FluentSpotify.Shared.Windows;

/// <summary>
/// Win32-утилиты для окон (DPI).
/// </summary>
public static class NativeWindowHelper
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>Коэффициент масштабирования окна (1.0 = 96 DPI).</summary>
    public static double GetScale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>Перевод DIP в физические пиксели для AppWindow.Resize/Move.</summary>
    public static int DipToPixels(IntPtr hwnd, double dip) => (int)Math.Round(dip * GetScale(hwnd));
}
