
using System.Diagnostics;

namespace FluentSpotify.Shared.Diagnostics;

/// <summary>
/// Простой файловый лог: %LOCALAPPDATA%\wshm\logs\&lt;процесс&gt;.log
/// </summary>
public static class AppLog
{
    private const long MaxLogSize = 1024 * 1024;
    private static readonly object _lock = new();
    private static readonly string _logPath;

    static AppLog()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wshm", "logs");
        string name = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "wshm";
        _logPath = Path.Combine(dir, name + ".log");

        try
        {
            Directory.CreateDirectory(dir);
            var info = new FileInfo(_logPath);
            if (info.Exists && info.Length > MaxLogSize)
            {
                File.Move(_logPath, _logPath + ".old", overwrite: true);
            }
        }
        catch
        {
            // Лог не должен ронять приложение
        }
    }

    public static void Write(string component, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{component}] {message}";
        Debug.WriteLine(line);
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Лог не должен ронять приложение
        }
    }

    public static void Write(string component, Exception ex) => Write(component, ex.ToString());
}
