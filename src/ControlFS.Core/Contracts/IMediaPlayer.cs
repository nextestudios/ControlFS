namespace ControlFS.Core.Contracts;

public enum MediaKind
{
    Audio,
    Video,
}

public enum MediaPlaybackState
{
    Opening,
    Buffering,
    Playing,
    Paused,
    Ended,
    Failed,
}

/// <summary>Faixa de áudio ou legenda que o reprodutor expõe (rótulo já pronto para a tela).</summary>
public sealed record MediaTrack(int Index, string Label);

/// <summary>
/// Retrato do reprodutor, lido pela aplicação a cada quadro (o reprodutor avisa em outras threads; ler um retrato evita
/// levar eventos para a thread de UI). <see cref="Subtitle"/> -1: legendas desligadas.
/// </summary>
public sealed record MediaStatus(
    MediaPlaybackState State,
    TimeSpan Position,
    TimeSpan Duration,
    double Volume,
    bool IsMuted,
    string? Error = null,
    IReadOnlyList<MediaTrack>? AudioTracks = null,
    int AudioTrack = -1,
    IReadOnlyList<MediaTrack>? Subtitles = null,
    int Subtitle = -1,
    bool HasVideo = false)
{
    public bool CanSeek => Duration > TimeSpan.Zero && State is not (MediaPlaybackState.Opening or MediaPlaybackState.Failed);
}

/// <summary>
/// Reprodução local de áudio e vídeo (#60, #61) com os codecs do próprio Windows. Só arquivos locais, abertos como fluxo
/// (nunca por URL): nada vai para a rede, nada é executado. Um codec que falta vira <see cref="MediaPlaybackState.Failed"/>
/// com uma mensagem clara.
/// </summary>
public interface IMediaPlayerFactory
{
    /// <summary>Abre e começa a tocar. <paramref name="subtitlePath"/>: legenda .srt/.vtt ao lado do vídeo, se houver.</summary>
    IMediaSession Open(string path, MediaKind kind, string? subtitlePath = null);
}

/// <summary>Uma reprodução. <see cref="IDisposable.Dispose"/> para o som e libera o arquivo na hora.</summary>
public interface IMediaSession : IDisposable
{
    MediaStatus Status { get; }

    /// <summary>Superfície de vídeo para a tela (o reprodutor do Windows); null em áudio ou em testes.</summary>
    object? VideoSurface { get; }

    void Play();

    void Pause();

    void Seek(TimeSpan position);

    /// <summary>Volume de 0 a 1.</summary>
    void SetVolume(double volume);

    void SetMuted(bool muted);

    void SelectAudioTrack(int index);

    /// <summary>-1 desliga as legendas.</summary>
    void SelectSubtitle(int index);
}
