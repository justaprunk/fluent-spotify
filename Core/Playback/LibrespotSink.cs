using System;
using System.Threading.Tasks;

namespace FluentSpotify.Core.Playback;

public class LibrespotSink : IAudioPlayer
{
    private PlaybackState _state = PlaybackState.Stopped;
    private int _volume = 100;
    private TimeSpan _position = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;

    public PlaybackState State => _state;
    public TimeSpan CurrentPosition => _position;
    public TimeSpan Duration => _duration;

    public int Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, 100);
    }

    public event Action<PlaybackState>? StateChanged;
    public event Action<TimeSpan>? PositionChanged;

    public Task PlayTrackAsync(string trackUri)
    {
        // Интеграция с нативным Spirc-сеансом librespot-core/WASAPI Sink
        _state = PlaybackState.Playing;
        _position = TimeSpan.Zero;
        StateChanged?.Invoke(_state);
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (_state == PlaybackState.Paused)
        {
            _state = PlaybackState.Playing;
            StateChanged?.Invoke(_state);
        }
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        if (_state == PlaybackState.Playing)
        {
            _state = PlaybackState.Paused;
            StateChanged?.Invoke(_state);
        }
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        _position = position;
        PositionChanged?.Invoke(_position);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _state = PlaybackState.Stopped;
        _position = TimeSpan.Zero;
        StateChanged?.Invoke(_state);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _state = PlaybackState.Stopped;
    }
}