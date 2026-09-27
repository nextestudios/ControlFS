using ControlFS.Core.Contracts;

namespace ControlFS.UnitTests.Support;

/// <summary>Reprodutor falso: guarda o que foi pedido e deixa o teste mexer no retrato (o real é o MediaPlayer do Windows).</summary>
public sealed class FakeMediaPlayer : IMediaPlayerFactory
{
    public List<(string Name, MediaKind Kind, string? Subtitle)> Opened { get; } = [];
    public FakeMediaSession? Last { get; private set; }
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(3);

    public IMediaSession Open(string path, MediaKind kind, string? subtitlePath = null)
    {
        Opened.Add((Path.GetFileName(path), kind, subtitlePath is null ? null : Path.GetFileName(subtitlePath)));
        return Last = new FakeMediaSession(Duration);
    }
}

public sealed class FakeMediaSession(TimeSpan duration) : IMediaSession
{
    public MediaStatus Status { get; set; } = new(MediaPlaybackState.Playing, TimeSpan.Zero, duration, 1, false);
    public bool Disposed { get; private set; }
    public object? VideoSurface => null;

    public void Play() => Status = Status with { State = MediaPlaybackState.Playing };

    public void Pause() => Status = Status with { State = MediaPlaybackState.Paused };

    public void Seek(TimeSpan position) => Status = Status with { Position = position, State = Status.State == MediaPlaybackState.Ended ? MediaPlaybackState.Paused : Status.State };

    public void SetVolume(double volume) => Status = Status with { Volume = volume };

    public void SetMuted(bool muted) => Status = Status with { IsMuted = muted };

    public void SelectAudioTrack(int index) => Status = Status with { AudioTrack = index };

    public void SelectSubtitle(int index) => Status = Status with { Subtitle = index };

    public void Dispose() => Disposed = true;
}
