using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.Core.Playback;

public enum SpotifyInstallKind
{
    NotInstalled,
    Desktop,        // %APPDATA%\Spotify\Spotify.exe (установщик с сайта)
    MicrosoftStore  // SpotifyAB.SpotifyMusic, запускается через алиас WindowsApps\Spotify.exe
}

/// <summary>
/// Запуск официального клиента Spotify в фоне: он нужен как устройство Spotify Connect,
/// которое воспроизводит звук, а интерфейсом остаётся наше приложение.
/// </summary>
public sealed class SpotifyClientLauncher
{
    private const string ProcessName = "Spotify";
    private static readonly TimeSpan WindowWaitTimeout = TimeSpan.FromSeconds(25);
    // Клиент может заново показать окно после входа/загрузки — следим ещё немного после первого скрытия
    private static readonly TimeSpan KeepHiddenFor = TimeSpan.FromSeconds(8);

    private readonly SemaphoreSlim _launchLock = new(1, 1);

    public static bool IsRunning => Process.GetProcessesByName(ProcessName).Length > 0;

    public static (SpotifyInstallKind Kind, string? Path) DetectInstall()
    {
        string desktop = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe");
        if (File.Exists(desktop)) return (SpotifyInstallKind.Desktop, desktop);

        // Алиас App Execution Alias Store-версии (файл нулевого размера, но запускается как exe)
        string storeAlias = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "Spotify.exe");
        if (File.Exists(storeAlias)) return (SpotifyInstallKind.MicrosoftStore, storeAlias);

        return (SpotifyInstallKind.NotInstalled, null);
    }

    /// <summary>
    /// Запустить клиент, если он не запущен, и спрятать его окно.
    /// Возвращает false, если клиент не установлен или не удалось запустить.
    /// Уже запущенный пользователем клиент не трогаем.
    /// </summary>
    public async Task<bool> EnsureRunningHiddenAsync(CancellationToken ct = default)
    {
        await _launchLock.WaitAsync(ct);
        try
        {
            if (IsRunning) return true;

            var (kind, path) = DetectInstall();
            if (path == null)
            {
                AppLog.Write("SpotifyClientLauncher", "Official Spotify client is not installed");
                return false;
            }

            try
            {
                // --minimized — тот же флаг, с которым клиент стартует при автозагрузке Windows
                Process.Start(new ProcessStartInfo(path, "--minimized") { UseShellExecute = false });
                AppLog.Write("SpotifyClientLauncher", $"Started {kind} client: {path}");
            }
            catch (Exception ex)
            {
                AppLog.Write("SpotifyClientLauncher", ex);
                return false;
            }

            // --minimized соблюдается не всегда (особенно Store-версией) — прячем окно сами
            await HideWindowsAsync(ct);
            return true;
        }
        finally
        {
            _launchLock.Release();
        }
    }

    private static async Task HideWindowsAsync(CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        TimeSpan? firstHiddenAt = null;

        while (!ct.IsCancellationRequested)
        {
            int hidden = HideVisibleSpotifyWindows();
            if (hidden > 0)
            {
                firstHiddenAt ??= started.Elapsed;
                AppLog.Write("SpotifyClientLauncher", $"Hid {hidden} Spotify window(s)");
            }

            var elapsed = started.Elapsed;
            if (firstHiddenAt is { } t && elapsed - t > KeepHiddenFor) break;
            if (firstHiddenAt == null && elapsed > WindowWaitTimeout) break; // Окна так и не появилось — и хорошо

            await Task.Delay(250, ct);
        }
    }

    private static int HideVisibleSpotifyWindows()
    {
        var pids = Process.GetProcessesByName(ProcessName).Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count == 0) return 0;

        int count = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pids.Contains(pid) && IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero
                && GetWindowTextLength(hwnd) > 0)
            {
                // SW_HIDE: окна нет ни на экране, ни на панели задач. Если пользователь откроет
                // Spotify из «Пуска», клиент сам покажет своё окно.
                ShowWindow(hwnd, SW_HIDE);
                count++;
            }
            return true;
        }, IntPtr.Zero);
        return count;
    }

    private const int SW_HIDE = 0;
    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);
}
