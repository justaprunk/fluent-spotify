using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentSpotify.Core.Api;
using FluentSpotify.Core.Auth;
using FluentSpotify.Core.Playback;
using FluentSpotify.Core.Settings;
using Microsoft.UI.Dispatching;
using SpotifyAPI.Web;
using Wshm.Common.Diagnostics;

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
        ? $"Коллекция • {TextFormat.Tracks(count)}"
        : "Коллекция";

    public string RedirectUri => PkceAuthService.RedirectUri;

    public ObservableCollection<FullPlaylist> Playlists { get; } = new();

    public PlayerBarViewModel PlayerBar { get; }

    private readonly PkceAuthService _authService;
    private readonly SettingsService _settings;
    private readonly BackgroundPlaybackService _background;

    public MainViewModel(SpotifyApiClient apiClient, PkceAuthService authService, SettingsService settings,
        BackgroundPlaybackService background, PlayerBarViewModel playerBar)
    {
        _apiClient = apiClient;
        _authService = authService;
        _settings = settings;
        _background = background;
        PlayerBar = playerBar;
        _isClientIdMissing = !authService.IsConfigured;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // Событие приходит из фонового потока опроса
        _apiClient.SessionExpired += () => RunOnUi(() =>
        {
            ResetState();
            ErrorMessage = "Сессия Spotify истекла. Войдите снова.";
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
            ErrorMessage = $"Не удалось подключиться к Spotify: {ex.Message}";
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

    [RelayCommand]
    private void SaveClientId()
    {
        string value = ClientIdInput.Trim();
        if (!AppSettings.IsValidClientId(value))
        {
            ErrorMessage = "Client ID должен состоять из 32 шестнадцатеричных символов (0–9, a–f). " +
                           "Скопируйте его из Spotify Developer Dashboard → ваше приложение → Settings.";
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

        Playlists.Clear();
        foreach (var playlist in userPlaylists)
        {
            Playlists.Add(playlist);
        }

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
}
