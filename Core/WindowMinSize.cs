using System.Runtime.InteropServices;

namespace FluentSpotify.Core;

/// <summary>
/// Минимальный размер окна через WM_GETMINMAXINFO.
/// В Windows App SDK 1.6 у OverlappedPresenter ещё нет PreferredMinimumWidth/Height.
/// </summary>
public sealed class WindowMinSize
{
    private const int GWLP_WNDPROC = -4;
    private const uint WM_GETMINMAXINFO = 0x0024;

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT Reserved;
        public POINT MaxSize;
        public POINT MaxPosition;
        public POINT MinTrackSize;
        public POINT MaxTrackSize;
    }

    private readonly double _minWidthDip;
    private readonly double _minHeightDip;
    private readonly WndProc _wndProc; // Держим ссылку, чтобы делегат не собрал GC
    private readonly IntPtr _previousWndProc;

    private WindowMinSize(IntPtr hwnd, double minWidthDip, double minHeightDip)
    {
        _minWidthDip = minWidthDip;
        _minHeightDip = minHeightDip;
        _wndProc = HookProc;
        _previousWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));
    }

    /// <summary>Размеры в DIP — пересчитываются под DPI текущего монитора.</summary>
    public static WindowMinSize Apply(IntPtr hwnd, double minWidthDip, double minHeightDip) =>
        new(hwnd, minWidthDip, minHeightDip);

    private IntPtr HookProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            double scale = Math.Max(1, GetDpiForWindow(hwnd)) / 96.0;
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.MinTrackSize.X = (int)Math.Round(_minWidthDip * scale);
            info.MinTrackSize.Y = (int)Math.Round(_minHeightDip * scale);
            Marshal.StructureToPtr(info, lParam, false);
        }
        return CallWindowProc(_previousWndProc, hwnd, msg, wParam, lParam);
    }
}
