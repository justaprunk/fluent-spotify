using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentSpotify.Core.Api;
using Microsoft.UI.Dispatching;
using SpotifyAPI.Web;

namespace FluentSpotify.ViewModels;

public partial class PlayerBarViewModel : ObservableObject
{
    private static readonly TimeSpan VolumeDebounce = TimeSpan.FromMilliseconds(300);

    private readonly SpotifyApiClient _apiClient;
    private readonly DispatcherQueue? _dispatcher;
    private bool _isUserSeeking;
    private bool _isApplyingRemoteState;
    private CancellationTokenSource? _volumeCts;

    [ObservableProperty]
    private string _trackTitle = "Не воспроизводится";

    [ObservableProperty]
    private string _artistName = "—";

    [ObservableProperty]
    private string? _albumArtUrl;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _currentPositionMs;

    [ObservableProperty]
    private double _durationMs = 1;

    [ObservableProperty]
    private string _positionText = "0:00";

    [ObservableProperty]
    private string _durationText = "0:00";

    [ObservableProperty]
    private double _volume = 50;

    [ObservableProperty]
    private bool _isShuffle;

    [ObservableProperty]
    private bool _isRepeat;

    public PlayerBarViewModel(SpotifyApiClient apiClient)
    {
        _apiClient = apiClient;
        // View-модель создаётся в UI-потоке; события API приходят из фонового —
        // изменения свойств нужно возвращать в UI-поток, иначе привязки падают с RPC_E_WRONG_THREAD
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _apiClient.PlaybackUpdated += playback =>
        {
            if (_dispatcher == null || _dispatcher.HasThreadAccess) ApplyPlayback(playback);
            else _dispatcher.TryEnqueue(() => ApplyPlayback(playback));
        };
    }

    private void ApplyPlayback(CurrentlyPlayingContext? playback)
    {
        _isApplyingRemoteState = true;
        try
        {
            if (playback == null || playback.Item is not FullTrack track)
            {
                TrackTitle = "Не воспроизводится";
                ArtistName = "—";
                AlbumArtUrl = null;
                IsPlaying = false;
                CurrentPositionMs = 0;
                DurationMs = 1;
                PositionText = "0:00";
                DurationText = "0:00";
                return;
            }

            TrackTitle = track.Name;
            ArtistName = string.Join(", ", track.Artists.Select(a => a.Name));
            AlbumArtUrl = track.Album?.Images?.FirstOrDefault()?.Url;
            IsPlaying = playback.IsPlaying;
            DurationMs = Math.Max(1, track.DurationMs);
            DurationText = FormatTime(TimeSpan.FromMilliseconds(track.DurationMs));

            if (!_isUserSeeking)
            {
                CurrentPositionMs = playback.ProgressMs;
                PositionText = FormatTime(TimeSpan.FromMilliseconds(playback.ProgressMs));
            }

            // Пока пользователь двигает ползунок громкости, не перетираем его значение
            if (playback.Device?.VolumePercent is int deviceVolume && _volumeCts == null)
            {
                Volume = deviceVolume;
            }

            IsShuffle = playback.ShuffleState;
            IsRepeat = playback.RepeatState != "off";
        }
        finally
        {
            _isApplyingRemoteState = false;
        }
    }

    partial void OnCurrentPositionMsChanged(double value)
    {
        if (_isUserSeeking)
        {
            PositionText = FormatTime(TimeSpan.FromMilliseconds(value));
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (_isApplyingRemoteState) return;

        // Debounce: ползунок генерирует десятки изменений за секунду
        _volumeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _volumeCts = cts;
        _ = SendVolumeAsync((int)Math.Round(value), cts);
    }

    private async Task SendVolumeAsync(int volume, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(VolumeDebounce, cts.Token);
            await _apiClient.SetVolumeAsync(volume);
        }
        catch (OperationCanceledException)
        {
            // Пришло новое значение
        }
        finally
        {
            if (_volumeCts == cts) _volumeCts = null;
            cts.Dispose();
        }
    }

    [RelayCommand]
    private async Task TogglePlayPauseAsync()
    {
        if (IsPlaying)
        {
            IsPlaying = false;
            await _apiClient.PauseAsync();
        }
        else
        {
            IsPlaying = true;
            await _apiClient.PlayAsync();
        }
    }

    [RelayCommand]
    private Task NextTrackAsync() => _apiClient.NextTrackAsync();

    [RelayCommand]
    private Task PreviousTrackAsync() => _apiClient.PreviousTrackAsync();

    [RelayCommand]
    private async Task ToggleShuffleAsync()
    {
        IsShuffle = !IsShuffle;
        await _apiClient.ToggleShuffleAsync(IsShuffle);
    }

    [RelayCommand]
    private async Task ToggleRepeatAsync()
    {
        IsRepeat = !IsRepeat;
        var mode = IsRepeat ? PlayerSetRepeatRequest.State.Context : PlayerSetRepeatRequest.State.Off;
        await _apiClient.SetRepeatModeAsync(mode);
    }

    /// <summary>Пользователь взялся за ползунок перемотки.</summary>
    public void BeginSeek() => _isUserSeeking = true;

    /// <summary>Пользователь отпустил ползунок перемотки.</summary>
    public async Task EndSeekAsync()
    {
        if (!_isUserSeeking) return;
        _isUserSeeking = false;
        PositionText = FormatTime(TimeSpan.FromMilliseconds(CurrentPositionMs));
        await _apiClient.SeekAsync((long)CurrentPositionMs);
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.Hours > 0
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }
}
