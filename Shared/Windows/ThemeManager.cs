using System.Text.Json;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.Shared.Windows;

/// <summary>
/// Читает ~/.config/wshm/theme.json, следит за его изменениями и уведомляет окна.
/// Событие ThemeChanged может прийти из фонового потока.
/// Упрощённая копия ThemeManager из wshm-ui-common: без синхронизации системной темы Windows
/// (её включают только процессы самого шелла).
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private static readonly Lazy<ThemeManager> _instance = new(() => new ThemeManager());
    public static ThemeManager Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _configDirectory;
    private readonly string _configFilePath;
    private readonly object _timerLock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    public ShellThemeConfig CurrentTheme { get; private set; } = new();
    public event Action<ShellThemeConfig>? ThemeChanged;

    private ThemeManager()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _configDirectory = Path.Combine(userProfile, ".config", "wshm");
        _configFilePath = Path.Combine(_configDirectory, "theme.json");

        EnsureConfigFileExists();
        LoadFromFile(raiseEvent: false);
        StartWatching();
    }

    private void EnsureConfigFileExists()
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            if (!File.Exists(_configFilePath))
            {
                File.WriteAllText(_configFilePath, JsonSerializer.Serialize(new ShellThemeConfig(), _jsonOptions));
            }
        }
        catch (Exception ex)
        {
            // Фоллбэк на значения по умолчанию при отсутствии прав доступа
            AppLog.Write("ThemeManager", ex);
        }
    }

    public void LoadFromFile() => LoadFromFile(raiseEvent: true);

    private void LoadFromFile(bool raiseEvent)
    {
        ShellThemeConfig? loaded = null;

        // Редактор может ещё держать файл открытым — несколько попыток
        for (int attempt = 0; attempt < 3 && loaded == null; attempt++)
        {
            try
            {
                if (!File.Exists(_configFilePath)) return;
                string json = File.ReadAllText(_configFilePath);
                loaded = JsonSerializer.Deserialize<ShellThemeConfig>(json, _jsonOptions);
                if (loaded == null) return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                AppLog.Write("ThemeManager", $"theme.json is invalid: {ex.Message}");
                return;
            }
        }

        if (loaded == null) return;

        CurrentTheme = loaded;
        if (raiseEvent)
        {
            ThemeChanged?.Invoke(loaded);
        }
    }

    private void StartWatching()
    {
        if (!Directory.Exists(_configDirectory)) return;

        _watcher = new FileSystemWatcher(_configDirectory, "theme.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileChanged; // Многие редакторы сохраняют через переименование
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: редакторы генерируют серию событий на одно сохранение
        lock (_timerLock)
        {
            _debounceTimer ??= new Timer(_ => LoadFromFile(), null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(200, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
