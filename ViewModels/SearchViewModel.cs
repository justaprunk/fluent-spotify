using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentSpotify.Core.Api;
using SpotifyAPI.Web;
using FluentSpotify.Shared.Diagnostics;

namespace FluentSpotify.ViewModels;

public sealed class TrackDisplayItem
{
    public string Id { get; set; } = string.Empty;
    public string Uri { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    public static TrackDisplayItem FromTrack(FullTrack track) => new()
    {
        Id = track.Id ?? string.Empty,
        Uri = track.Uri ?? string.Empty,
        Title = track.Name ?? string.Empty,
        Artist = string.Join(", ", track.Artists.Select(a => a.Name)),
        Album = track.Album?.Name ?? string.Empty,
        Duration = TimeSpan.FromMilliseconds(track.DurationMs).ToString(@"m\:ss"),
        ImageUrl = track.Album?.Images?.FirstOrDefault()?.Url
    };
}

public partial class SearchViewModel : ObservableObject
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    private readonly SpotifyApiClient _apiClient;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<TrackDisplayItem> Tracks { get; } = new();
    public ObservableCollection<SimpleAlbum> Albums { get; } = new();
    public ObservableCollection<FullArtist> Artists { get; } = new();

    public SearchViewModel(SpotifyApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    async partial void OnQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            ClearResults();
            IsLoading = false;
            ErrorMessage = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;

        try
        {
            // Debounce, чтобы не спамить API запросами при быстром вводе
            await Task.Delay(Debounce, token);
            await PerformSearchAsync(value.Trim(), token);
        }
        catch (OperationCanceledException)
        {
            // Запрос отменён новым вводом пользователя
        }
        catch (Exception ex)
        {
            AppLog.Write("SearchViewModel", ex);
            if (!token.IsCancellationRequested)
            {
                ErrorMessage = Localizer.Format("Search_Error", ex.Message);
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task PlayTrackAsync(TrackDisplayItem? track)
    {
        if (track == null || string.IsNullOrEmpty(track.Uri)) return;
        await _apiClient.PlayAsync(trackUri: track.Uri);
    }

    [RelayCommand]
    private async Task PlayAlbumAsync(SimpleAlbum? album)
    {
        if (album == null || string.IsNullOrEmpty(album.Uri)) return;
        await _apiClient.PlayAsync(contextUri: album.Uri);
    }

    private async Task PerformSearchAsync(string queryText, CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = null;

        var response = await _apiClient.SearchAsync(queryText);
        ct.ThrowIfCancellationRequested();

        ClearResults();

        foreach (var track in response?.Tracks?.Items ?? Enumerable.Empty<FullTrack>())
        {
            Tracks.Add(TrackDisplayItem.FromTrack(track));
        }

        foreach (var album in response?.Albums?.Items ?? Enumerable.Empty<SimpleAlbum>())
        {
            Albums.Add(album);
        }

        foreach (var artist in response?.Artists?.Items ?? Enumerable.Empty<FullArtist>())
        {
            Artists.Add(artist);
        }

        HasResults = Tracks.Count > 0 || Albums.Count > 0 || Artists.Count > 0;
        IsLoading = false;
    }

    private void ClearResults()
    {
        Tracks.Clear();
        Albums.Clear();
        Artists.Clear();
        HasResults = false;
    }
}
