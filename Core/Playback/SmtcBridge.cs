using FluentSpotify.Core.Api;
using FluentSpotify.Core.Settings;
using SpotifyAPI.Web;
using Wshm.Common.Diagnostics;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace FluentSpotify.Core.Playback;

/// <summary>
/// Связывает системные медиаклавиши и оверлей громкости Windows (SMTC) с Spotify Web API.
/// </summary>
public class SmtcBridge : IDisposable
{
    private readonly SpotifyApiClient _apiClient;
    // BackgroundMediaPlayer — API только для UWP и не работает в desktop-приложении.
    // Для desktop SMTC берётся у экземпляра MediaPlayer с отключённым CommandManager.
    private readonly MediaPlayer _mediaPlayer;
    private readonly SystemMediaTransportControls _smtc;
    private string? _currentTrackId;

    private bool _enabled;

    public SmtcBridge(SpotifyApiClient apiClient, SettingsService settings)
    {
        _apiClient = apiClient;

        _mediaPlayer = new MediaPlayer();
        _mediaPlayer.CommandManager.IsEnabled = false;

        _smtc = _mediaPlayer.SystemMediaTransportControls;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.IsEnabled = true;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;

        _smtc.ButtonPressed += OnSmtcButtonPressed;
        _apiClient.PlaybackUpdated += OnPlaybackUpdated;

        // Когда звук играет официальный клиент в фоне, он сам показывает карточку
        // «Сейчас играет» в Windows — наша была бы дублем
        SetEnabled(!settings.Current.LaunchClientInBackground);
        settings.Changed += s => SetEnabled(!s.LaunchClientInBackground);
    }

    private void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _smtc.IsEnabled = enabled;
        if (!enabled)
        {
            _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            _smtc.DisplayUpdater.ClearAll();
            _smtc.DisplayUpdater.Update();
            _currentTrackId = null;
        }
    }

    private async void OnSmtcButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        try
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    await _apiClient.PlayAsync();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    await _apiClient.PauseAsync();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    await _apiClient.NextTrackAsync();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    await _apiClient.PreviousTrackAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("SmtcBridge", ex);
        }
    }

    private void OnPlaybackUpdated(CurrentlyPlayingContext? playback)
    {
        if (!_enabled) return;

        if (playback == null || playback.Item is not FullTrack track)
        {
            _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            _currentTrackId = null;
            return;
        }

        _smtc.PlaybackStatus = playback.IsPlaying
            ? MediaPlaybackStatus.Playing
            : MediaPlaybackStatus.Paused;

        // Метаданные и обложку обновляем только при смене трека, а не на каждом тике опроса
        if (track.Id != _currentTrackId)
        {
            _currentTrackId = track.Id;

            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = track.Name ?? string.Empty;
            updater.MusicProperties.Artist = string.Join(", ", track.Artists.Select(a => a.Name));
            updater.MusicProperties.AlbumTitle = track.Album?.Name ?? string.Empty;

            var albumImage = track.Album?.Images?.FirstOrDefault();
            updater.Thumbnail = albumImage != null && Uri.TryCreate(albumImage.Url, UriKind.Absolute, out var imageUri)
                ? RandomAccessStreamReference.CreateFromUri(imageUri)
                : null;

            updater.Update();
        }

        var timeline = new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            MinSeekTime = TimeSpan.Zero,
            Position = TimeSpan.FromMilliseconds(playback.ProgressMs),
            EndTime = TimeSpan.FromMilliseconds(track.DurationMs),
            MaxSeekTime = TimeSpan.FromMilliseconds(track.DurationMs)
        };
        _smtc.UpdateTimelineProperties(timeline);
    }

    public void Dispose()
    {
        _smtc.ButtonPressed -= OnSmtcButtonPressed;
        _apiClient.PlaybackUpdated -= OnPlaybackUpdated;
        _smtc.IsEnabled = false;
        _mediaPlayer.Dispose();
    }
}
