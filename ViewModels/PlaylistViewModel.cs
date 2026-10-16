using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentSpotify.Core.Api;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.ViewModels;

/// <summary>
/// Страница списка треков: обычный плейлист или «Любимые треки» (id = SpotifyApiClient.LikedSongsId).
/// </summary>
public partial class PlaylistViewModel : ObservableObject
{
    private readonly SpotifyApiClient _apiClient;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private string _playlistId = string.Empty;

    [ObservableProperty]
    private string _playlistUri = string.Empty;

    [ObservableProperty]
    private string _kindLabel = "ПЛЕЙЛИСТ";

    [ObservableProperty]
    private string _name = "Загрузка...";

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _coverUrl;

    [ObservableProperty]
    private bool _isLikedSongs;

    [ObservableProperty]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalTracksText))]
    private int _totalTracks;

    public string TotalTracksText => TextFormat.Tracks(TotalTracks);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public ObservableCollection<TrackDisplayItem> Tracks { get; } = new();

    public PlaylistViewModel(SpotifyApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task LoadAsync(string playlistId)
    {
        if (string.IsNullOrWhiteSpace(playlistId)) return;

        // Отменяем загрузку предыдущего плейлиста, если пользователь быстро переключился
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var ct = cts.Token;

        PlaylistId = playlistId;
        PlaylistUri = string.Empty;
        IsLoading = true;
        ErrorMessage = null;
        Tracks.Clear();

        try
        {
            IAsyncEnumerable<IReadOnlyList<SpotifyAPI.Web.FullTrack>> pages;

            if (playlistId == SpotifyApiClient.LikedSongsId)
            {
                IsLikedSongs = true;
                KindLabel = "КОЛЛЕКЦИЯ";
                Name = "Любимые треки";
                Description = null;
                CoverUrl = null;
                OwnerName = "Вы";
                TotalTracks = await _apiClient.GetSavedTracksTotalAsync();
                PlaylistUri = await _apiClient.GetLikedSongsContextUriAsync() ?? string.Empty;
                pages = _apiClient.EnumerateSavedTracksAsync(ct);
            }
            else
            {
                IsLikedSongs = false;
                KindLabel = "ПЛЕЙЛИСТ";
                var details = await _apiClient.GetPlaylistDetailsAsync(playlistId);
                if (details == null || ct.IsCancellationRequested) return;

                PlaylistUri = details.Uri;
                Name = details.Name;
                Description = string.IsNullOrWhiteSpace(details.Description) ? null : details.Description;
                CoverUrl = details.ImageUrl;
                OwnerName = details.OwnerName;
                TotalTracks = details.TotalItems;

                if (!details.ContentsAvailable)
                {
                    ErrorMessage = "Spotify не отдаёт треки чужих плейлистов приложениям в режиме разработки " +
                                   "(ограничение Web API с февраля 2026). Плейлист можно воспроизвести целиком кнопкой ▶.";
                    return;
                }

                pages = _apiClient.EnumeratePlaylistTracksAsync(playlistId, ct);
            }

            // Добавляем треки по мере загрузки страниц — длинные плейлисты появляются сразу
            await foreach (var page in pages.WithCancellation(ct))
            {
                if (ct.IsCancellationRequested) return;
                foreach (var track in page)
                {
                    Tracks.Add(TrackDisplayItem.FromTrack(track));
                }
            }

            if (TotalTracks < Tracks.Count) TotalTracks = Tracks.Count;
        }
        catch (OperationCanceledException)
        {
            // Пользователь ушёл на другую страницу
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            AppLog.Write("PlaylistViewModel", ex);
            if (!ct.IsCancellationRequested)
            {
                ErrorMessage = "Spotify запретил доступ к этому плейлисту (403). " +
                               "Для приложений в режиме разработки доступны только ваши плейлисты и плейлисты, где вы соавтор.";
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("PlaylistViewModel", ex);
            if (!ct.IsCancellationRequested)
            {
                ErrorMessage = $"Не удалось загрузить треки: {ex.Message}";
            }
        }
        finally
        {
            if (_loadCts == cts)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Остановить загрузку (при уходе со страницы).</summary>
    public void CancelLoading() => _loadCts?.Cancel();

    [RelayCommand]
    private async Task PlayPlaylistAsync()
    {
        if (!string.IsNullOrEmpty(PlaylistUri))
        {
            await _apiClient.PlayAsync(contextUri: PlaylistUri);
        }
        else if (Tracks.Count > 0)
        {
            await _apiClient.PlayAsync(trackUri: Tracks[0].Uri);
        }
    }

    [RelayCommand]
    private async Task PlayTrackAsync(TrackDisplayItem? track)
    {
        if (track == null || string.IsNullOrEmpty(track.Uri)) return;

        // Воспроизведение трека внутри контекста текущего плейлиста / коллекции
        if (!string.IsNullOrEmpty(PlaylistUri))
        {
            await _apiClient.PlayAsync(contextUri: PlaylistUri, trackUri: track.Uri);
        }
        else
        {
            await _apiClient.PlayAsync(trackUri: track.Uri);
        }
    }
}
