using System;
using System.Threading.Tasks;

namespace FluentSpotify.Core.Playback;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
    Buffering
}

public interface IAudioPlayer : IDisposable
{
    PlaybackState State { get; }
    TimeSpan CurrentPosition { get; }
    TimeSpan Duration { get; }
    int Volume { get; set; }

    event Action<PlaybackState>? StateChanged;
    event Action<TimeSpan>? PositionChanged;

    Task PlayTrackAsync(string trackUri);
    Task ResumeAsync();
    Task PauseAsync();
    Task SeekAsync(TimeSpan position);
    Task StopAsync();
}