using System.Text.Json;
using System.Text.Json.Serialization;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.Core.Library;

/// <summary>
/// Папка медиатеки: вложенные папки и плейлисты (id Spotify) в заданном пользователем порядке.
/// Корень дерева — сама медиатека, у него нет названия.
/// </summary>
public sealed class PlaylistFolder
{
    public string Id { get; set; } = NewId();

    public string Name { get; set; } = string.Empty;

    /// <summary>Папка раскрыта в боковой панели.</summary>
    public bool IsExpanded { get; set; }

    public List<PlaylistFolder> Folders { get; set; } = new();

    public List<string> Playlists { get; set; } = new();

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Число плейлистов в папке вместе с вложенными.</summary>
    [JsonIgnore]
    public int TotalPlaylists => Playlists.Count + Folders.Sum(f => f.TotalPlaylists);

    /// <summary>Папка и все вложенные в неё, сверху вниз.</summary>
    public IEnumerable<PlaylistFolder> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Folders)
        {
            foreach (var folder in child.SelfAndDescendants()) yield return folder;
        }
    }
}

/// <summary>
/// Папки плейлистов. Web API Spotify папок не отдаёт, поэтому они живут только в Fluent Spotify:
/// %LOCALAPPDATA%\FluentSpotify\folders.json, отдельное дерево для каждого аккаунта.
/// Все методы вызываются из UI-потока.
/// </summary>
public sealed class PlaylistFolderService
{
    public const string RootId = "root";
    private const string ExportFormat = "fluent-spotify-folders";
    private const string DefaultAccount = "default";

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FluentSpotify",
        "folders.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private FolderStore _store = new();
    private string? _account;
    private List<string> _knownPlaylists = new();

    /// <summary>Корень медиатеки текущего аккаунта.</summary>
    public PlaylistFolder Root { get; private set; } = NewRoot();

    /// <summary>Структура папок изменилась — панель и главная перестраиваются.</summary>
    public event Action? Changed;

    public PlaylistFolderService()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                _store = JsonSerializer.Deserialize<FolderStore>(File.ReadAllText(StorePath), JsonOptions) ?? new FolderStore();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("PlaylistFolders", ex);
        }
        _store.Accounts ??= new();
    }

    /// <summary>
    /// Сверка с актуальным списком плейлистов (в порядке Spotify): удалённые убираются из папок,
    /// новые встают в начало корня — как в официальном клиенте. Для нового аккаунта корень
    /// просто повторяет порядок Spotify.
    /// </summary>
    public void Reconcile(string? userId, IReadOnlyList<string> playlistIds)
    {
        // Без id пользователя (сбой /me) остаёмся в дереве уже открытого аккаунта
        string account = userId ?? _account ?? DefaultAccount;
        if (account != _account || !_store.Accounts.ContainsKey(account))
        {
            _account = account;
            if (_store.Accounts.TryGetValue(account, out var saved) && saved != null)
            {
                Root = Normalize(saved);
            }
            else
            {
                Root = NewRoot();
                Root.Playlists.AddRange(playlistIds.Distinct());
            }
            _store.Accounts[account] = Root;
        }

        _knownPlaylists = playlistIds.Distinct().ToList();
        ApplyPlaylists(Root, _knownPlaylists);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Выход из аккаунта: дерево остаётся на диске, в памяти — пусто.</summary>
    public void Reset()
    {
        _account = null;
        _knownPlaylists = new();
        Root = NewRoot();
        Changed?.Invoke();
    }

    #region Поиск

    public PlaylistFolder? FindFolder(string id) =>
        Root.SelfAndDescendants().FirstOrDefault(f => f.Id == id);

    public PlaylistFolder? FindParent(PlaylistFolder folder) =>
        Root.SelfAndDescendants().FirstOrDefault(f => f.Folders.Contains(folder));

    /// <summary>Папка, в которой лежит плейлист (корень — если не в папке).</summary>
    public PlaylistFolder? FindPlaylistFolder(string playlistId) =>
        Root.SelfAndDescendants().FirstOrDefault(f => f.Playlists.Contains(playlistId));

    /// <summary>Все папки (без корня) с глубиной вложенности, в порядке дерева.</summary>
    public IEnumerable<(PlaylistFolder Folder, int Depth)> Flatten() => Flatten(Root, 0);

    private static IEnumerable<(PlaylistFolder, int)> Flatten(PlaylistFolder parent, int depth)
    {
        foreach (var folder in parent.Folders)
        {
            yield return (folder, depth);
            foreach (var nested in Flatten(folder, depth + 1)) yield return nested;
        }
    }

    /// <summary>Путь папки для заголовков: «Рок › 80-е».</summary>
    public string PathOf(PlaylistFolder folder)
    {
        var names = new List<string>();
        for (var current = folder; current != null && current.Id != RootId; current = FindParent(current))
        {
            names.Insert(0, current.Name);
        }
        return string.Join(" › ", names);
    }

    /// <summary>Папку можно переложить в target: не в саму себя и не во вложенную.</summary>
    public bool CanMoveFolder(PlaylistFolder folder, PlaylistFolder target) =>
        !folder.SelfAndDescendants().Contains(target);

    #endregion

    #region Изменения

    public PlaylistFolder CreateFolder(PlaylistFolder parent, string name)
    {
        var folder = new PlaylistFolder { Name = CleanName(name) };
        parent.Folders.Add(folder);
        parent.IsExpanded = true;
        Commit();
        return folder;
    }

    public void Rename(PlaylistFolder folder, string name)
    {
        folder.Name = CleanName(name);
        Commit();
    }

    /// <summary>Удаляет папку; её плейлисты и вложенные папки переходят в родительскую.</summary>
    public void Delete(PlaylistFolder folder)
    {
        if (FindParent(folder) is not { } parent) return;

        int index = parent.Folders.IndexOf(folder);
        parent.Folders.RemoveAt(index);
        parent.Folders.InsertRange(index, folder.Folders);
        parent.Playlists.AddRange(folder.Playlists);
        Commit();
    }

    /// <summary>Переносит плейлист в папку: перед плейлистом before или в конец.</summary>
    public void MovePlaylist(string playlistId, PlaylistFolder target, string? before = null)
    {
        if (playlistId == before) return;

        FindPlaylistFolder(playlistId)?.Playlists.Remove(playlistId);
        int index = before != null ? target.Playlists.IndexOf(before) : -1;
        if (index >= 0) target.Playlists.Insert(index, playlistId);
        else target.Playlists.Add(playlistId);
        Commit();
    }

    /// <summary>Переносит папку в другую: перед папкой before или в конец.</summary>
    public void MoveFolder(PlaylistFolder folder, PlaylistFolder target, PlaylistFolder? before = null)
    {
        if (folder == before || !CanMoveFolder(folder, target)) return;

        FindParent(folder)?.Folders.Remove(folder);
        int index = before != null ? target.Folders.IndexOf(before) : -1;
        if (index >= 0) target.Folders.Insert(index, folder);
        else target.Folders.Add(folder);
        target.IsExpanded |= target.Id != RootId;
        Commit();
    }

    /// <summary>Сдвиг плейлиста внутри своей папки: −1 — выше, +1 — ниже.</summary>
    public void ShiftPlaylist(string playlistId, int delta)
    {
        if (FindPlaylistFolder(playlistId) is { } folder && Shift(folder.Playlists, playlistId, delta)) Commit();
    }

    public void ShiftFolder(PlaylistFolder folder, int delta)
    {
        if (FindParent(folder) is { } parent && Shift(parent.Folders, folder, delta)) Commit();
    }

    public bool CanShiftPlaylist(string playlistId, int delta) =>
        FindPlaylistFolder(playlistId) is { } folder && CanShift(folder.Playlists, playlistId, delta);

    public bool CanShiftFolder(PlaylistFolder folder, int delta) =>
        FindParent(folder) is { } parent && CanShift(parent.Folders, folder, delta);

    /// <summary>Раскрытие папки в панели: сохраняется без перестройки панели.</summary>
    public void SetExpanded(string folderId, bool expanded)
    {
        if (FindFolder(folderId) is { } folder && folder.IsExpanded != expanded)
        {
            folder.IsExpanded = expanded;
            Save();
        }
    }

    #endregion

    #region Экспорт и импорт

    public void Export(string path)
    {
        var data = new FolderExport { Format = ExportFormat, Version = 1, Exported = DateTimeOffset.Now, Root = Root };
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
    }

    /// <summary>
    /// Заменяет папки текущего аккаунта папками из файла. Плейлисты, которых нет в медиатеке,
    /// отбрасываются; плейлисты, которых нет в файле, остаются в корне.
    /// </summary>
    /// <returns>Число загруженных папок.</returns>
    /// <exception cref="InvalidDataException">Файл не является экспортом папок.</exception>
    public int Import(string path)
    {
        FolderExport? data;
        try
        {
            data = JsonSerializer.Deserialize<FolderExport>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
        if (data?.Format != ExportFormat || data.Root == null)
        {
            throw new InvalidDataException();
        }

        var root = Normalize(data.Root);
        ApplyPlaylists(root, _knownPlaylists);
        Root = root;
        _store.Accounts[_account ?? DefaultAccount] = Root;
        Commit();
        return Flatten().Count();
    }

    public bool HasFolders => Root.Folders.Count > 0;

    #endregion

    private void Commit()
    {
        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        if (_account == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            // Через временный файл: сбой посреди записи не должен стереть папки
            string temp = StorePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_store, JsonOptions));
            File.Move(temp, StorePath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Write("PlaylistFolders", ex);
        }
    }

    /// <summary>Убирает отсутствующие и повторяющиеся плейлисты, новые ставит в начало корня.</summary>
    private static void ApplyPlaylists(PlaylistFolder root, IReadOnlyList<string> playlistIds)
    {
        var alive = playlistIds.ToHashSet();
        var seen = new HashSet<string>();
        foreach (var folder in root.SelfAndDescendants())
        {
            folder.Playlists.RemoveAll(id => !alive.Contains(id) || !seen.Add(id));
        }
        root.Playlists.InsertRange(0, playlistIds.Where(id => !seen.Contains(id)));
    }

    /// <summary>Приводит загруженное дерево в порядок: пустые списки, повторы id, названия.</summary>
    private static PlaylistFolder Normalize(PlaylistFolder root)
    {
        root.Id = RootId;
        root.Name = string.Empty;
        var ids = new HashSet<string> { RootId };
        foreach (var folder in root.SelfAndDescendants())
        {
            folder.Folders = (folder.Folders ?? new()).Where(f => f != null).ToList();
            folder.Playlists = (folder.Playlists ?? new()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (folder != root)
            {
                if (string.IsNullOrWhiteSpace(folder.Id) || !ids.Add(folder.Id))
                {
                    folder.Id = PlaylistFolder.NewId();
                    ids.Add(folder.Id);
                }
                folder.Name = CleanName(folder.Name);
            }
        }
        return root;
    }

    private static string CleanName(string? name)
    {
        string clean = (name ?? string.Empty).Trim();
        if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength];
        return clean.Length > 0 ? clean : Localizer.Get("Folder_DefaultName");
    }

    public const int MaxNameLength = 100;

    private static bool CanShift<T>(List<T> list, T item, int delta)
    {
        int index = list.IndexOf(item);
        return index >= 0 && index + delta >= 0 && index + delta < list.Count;
    }

    private static bool Shift<T>(List<T> list, T item, int delta)
    {
        if (!CanShift(list, item, delta)) return false;
        int index = list.IndexOf(item);
        list.RemoveAt(index);
        list.Insert(index + delta, item);
        return true;
    }

    private static PlaylistFolder NewRoot() => new() { Id = RootId, IsExpanded = true };

    private sealed class FolderStore
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, PlaylistFolder> Accounts { get; set; } = new();
    }

    private sealed class FolderExport
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public DateTimeOffset? Exported { get; set; }
        public PlaylistFolder? Root { get; set; }
    }
}
