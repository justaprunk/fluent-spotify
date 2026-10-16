using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentSpotify.Core.Api;
using FluentSpotify.Core.Auth;
using FluentSpotify.Core.Library;
using FluentSpotify.Core.Playback;
using FluentSpotify.Core.Settings;
using Microsoft.UI.Dispatching;
using SpotifyAPI.Web;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SpotifyApiClient _apiClient;
    private readonly DispatcherQueue? _dispatcher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotAuthenticated))]
    private bool _isAuthenticated;

    // Для экрана входа: x:Bind-конвертеры недоступны в окне-наследнике FluentWindow
    public bool IsNotAuthenticated => !IsAuthenticated;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClientIdConfigured))]
    private bool _isClientIdMissing;

    [ObservableProperty]
    private string _clientIdInput = string.Empty;

    public bool IsClientIdConfigured => !IsClientIdMissing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikedTracksText))]
    private int? _likedTracksCount;

    public string LikedTracksText => LikedTracksCount is int count
        ? Localizer.Format("Home_CollectionTracks", TextFormat.Tracks(count))
        : Localizer.Get("Home_Collection");

    public string RedirectUri => PkceAuthService.RedirectUri;

    public ObservableCollection<FullPlaylist> Playlists { get; } = new();

    /// <summary>Разделы сетки плейлистов на главной: без папки и по одному на каждую папку.</summary>
    public ObservableCollection<PlaylistSection> HomeSections { get; } = new();

    /// <summary>Локальные папки плейлистов (Web API Spotify папок не знает).</summary>
    public PlaylistFolderService Folders { get; }

    public PlayerBarViewModel PlayerBar { get; }

    private readonly PkceAuthService _authService;
    private readonly SettingsService _settings;
    private readonly BackgroundPlaybackService _background;

    public MainViewModel(SpotifyApiClient apiClient, PkceAuthService authService, SettingsService settings,
        BackgroundPlaybackService background, PlayerBarViewModel playerBar, PlaylistFolderService folders)
    {
        Folders = folders;
        Folders.Changed += RebuildHomeSections;
        _apiClient = apiClient;
        _authService = authService;
        _settings = settings;
        _background = background;
        PlayerBar = playerBar;
        _isClientIdMissing = !authService.IsConfigured;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _groupHomeByFolders = settings.Current.GroupHomeByFolders;
        settings.Changed += s =>
        {
            if (s.GroupHomeByFolders == _groupHomeByFolders) return;
            _groupHomeByFolders = s.GroupHomeByFolders;
            RunOnUi(RebuildHomeSections);
        };

        // Событие приходит из фонового потока опроса
        _apiClient.SessionExpired += () => RunOnUi(() =>
        {
            ResetState();
            ErrorMessage = Localizer.Get("Main_SessionExpired");
        });
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher == null || _dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _apiClient.InitializeAsync();
            IsAuthenticated = _apiClient.IsAuthenticated;

            if (IsAuthenticated)
            {
                // Параллельно поднимаем скрытый официальный клиент как устройство воспроизведения
                _ = _background.PrepareAsync();
                await LoadPlaylistsAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("MainViewModel", ex);
            ErrorMessage = Localizer.Format("Main_ConnectFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _apiClient.AuthenticateAsync();
            IsAuthenticated = _apiClient.IsAuthenticated;

            if (IsAuthenticated)
            {
                // Параллельно поднимаем скрытый официальный клиент как устройство воспроизведения
                _ = _background.PrepareAsync();
                await LoadPlaylistsAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("MainViewModel", ex);
            IsAuthenticated = false;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Повторный вход без выхода из аккаунта: нужен, когда приложению понадобились новые скоупы.
    /// В отличие от LoginCommand, при ошибке/отмене текущая сессия остаётся рабочей.
    /// </summary>
    public async Task ReauthenticateAsync()
    {
        await _apiClient.AuthenticateAsync();
        await RefreshPlaylistsAsync();
    }

    [RelayCommand]
    private void SaveClientId()
    {
        string value = ClientIdInput.Trim();
        if (!AppSettings.IsValidClientId(value))
        {
            ErrorMessage = Localizer.Get("Main_InvalidClientId");
            return;
        }

        _settings.Update(s => s.ClientId = value);
        _authService.SetClientId(value);
        IsClientIdMissing = false;
        ClientIdInput = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void EditClientId()
    {
        ErrorMessage = null;
        IsClientIdMissing = true;
    }

    [RelayCommand]
    private void Logout()
    {
        _apiClient.Logout();
        ResetState();
    }

    private void ResetState()
    {
        IsAuthenticated = false;
        Playlists.Clear();
        Folders.Reset();
        LikedTracksCount = null;
    }

    [RelayCommand]
    private async Task RefreshPlaylistsAsync()
    {
        if (!IsAuthenticated) return;
        try
        {
            await LoadPlaylistsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write("MainViewModel", ex);
        }
    }

    private async Task LoadPlaylistsAsync()
    {
        var userPlaylists = await _apiClient.GetUserPlaylistsAsync();

        string? userId = null;
        try
        {
            userId = await _apiClient.GetCurrentUserIdAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write("MainViewModel", ex);
        }

        Playlists.Clear();
        foreach (var playlist in userPlaylists)
        {
            Playlists.Add(playlist);
        }

        // Сверка папок с медиатекой; Changed перестроит боковую панель и главную
        Folders.Reconcile(userId, userPlaylists.Select(p => p.Id).OfType<string>().ToList());

        try
        {
            LikedTracksCount = await _apiClient.GetSavedTracksTotalAsync();
        }
        catch (Exception ex)
        {
            // Счётчик на карточке не критичен — без него просто "Коллекция"
            AppLog.Write("MainViewModel", ex);
        }
    }

    public FullPlaylist? FindPlaylist(string id) => Playlists.FirstOrDefault(p => p.Id == id);

    private bool _groupHomeByFolders;

    private void RebuildHomeSections()
    {
        HomeSections.Clear();
        if (Playlists.Count == 0) return;

        var title = Localizer.Get("Home_YourPlaylists");
        if (!_groupHomeByFolders || !Folders.HasFolders)
        {
            HomeSections.Add(new PlaylistSection(title, string.Empty, Playlists.ToList()));
            return;
        }

        var root = Resolve(Folders.Root.Playlists);
        if (root.Count > 0)
        {
            HomeSections.Add(new PlaylistSection(title, string.Empty, root));
        }
        foreach (var (folder, _) in Folders.Flatten())
        {
            var playlists = Resolve(folder.Playlists);
            if (playlists.Count == 0) continue;
            HomeSections.Add(new PlaylistSection(Folders.PathOf(folder), Localizer.Plural("Playlists", playlists.Count), playlists));
        }
    }

    private List<FullPlaylist> Resolve(IEnumerable<string> ids)
    {
        var byId = Playlists.Where(p => p.Id != null).ToDictionary(p => p.Id!);
        return ids.Select(id => byId.GetValueOrDefault(id)).OfType<FullPlaylist>().ToList();
    }
}

/// <summary>Раздел сетки на главной: заголовок (путь папки) и её плейлисты.</summary>
public sealed record PlaylistSection(string Title, string Subtitle, IReadOnlyList<FullPlaylist> Playlists);