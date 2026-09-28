using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Preview;

namespace ControlFS.Application;

/// <summary>
/// Reprodução interna de áudio (#60) com os codecs do Windows. O conteúdo é conferido fora da thread de UI antes de chegar
/// ao reprodutor; o estado vem de um retrato lido a cada quadro; fechar para o som e libera o arquivo.
/// </summary>
public sealed partial class AppController
{
    /// <summary>Passo de Esquerda/Direita.</summary>
    public static readonly TimeSpan MediaSeekStep = TimeSpan.FromSeconds(10);

    /// <summary>Passo de LB/RB.</summary>
    public static readonly TimeSpan MediaLongSeekStep = TimeSpan.FromMinutes(1);

    /// <summary>Passo de Cima/Baixo no volume.</summary>
    public const double MediaVolumeStep = 0.1;

    /// <summary>Reprodutor (#60); null: a reprodução interna não existe nesta compilação.</summary>
    public IMediaPlayerFactory? MediaPlayer { get; init; }

    internal static bool IsPlayableAudio(FileEntry entry) =>
        entry is { Kind: EntryKind.File, FullPath: not null, IsBlocked: false } && MediaPreviewPolicy.IsAudioExtension(entry.Extension);

    private string? MediaUnavailable => MediaPlayer is null ? "Reprodução interna indisponível nesta compilação." : null;

    internal void OpenAudioPreview(PaneState pane, FileEntry entry)
    {
        if (MediaPlayer is null || entry.FullPath is null) return;
        var modal = new AudioPreviewModal(pane, entry);
        PushModal(modal);
        Track(StartMediaAsync(modal, MediaKind.Audio, null));
    }

    private async Task StartMediaAsync(MediaPreviewModal modal, MediaKind kind, string? subtitlePath)
    {
        var path = modal.Entry.FullPath!;
        var factory = MediaPlayer!;
        try
        {
            var session = await Task.Run(() =>
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096))
                    MediaPreviewPolicy.Inspect(stream);
                return factory.Open(path, kind, subtitlePath);
            });
            if (modal.IsClosed)
            {
                session.Dispose();
                return;
            }
            modal.Session = session;
            modal.Status = session.Status;
        }
        catch (PreviewException ex)
        {
            modal.Error = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            modal.Error = "Não foi possível ler o arquivo: " + ErrorText(ex, "Áudio");
        }
        if (!modal.IsClosed) RaiseChanged();
    }

    /// <summary>
    /// Chamado a cada quadro: lê o retrato dos reprodutores abertos (mesmo sob um diálogo) e só redesenha quando algo que
    /// a tela mostra mudou (estado, segundo da posição, duração, volume, faixas, erro).
    /// </summary>
    private void TickMedia()
    {
        var changed = false;
        foreach (var modal in _modals)
        {
            if (modal is not MediaPreviewModal { Session: { } session } media) continue;
            var status = session.Status;
            if (Visible(status) == Visible(media.Status)) continue;
            media.Status = status;
            changed = true;
        }
        if (changed) RaiseChanged();
        foreach (var video in _modals.OfType<VideoPlayerModal>().ToList()) TickVideo(video);
    }

    /// <summary>O que a tela mostra de um retrato: a posição conta por segundo inteiro.</summary>
    private static (MediaPlaybackState, long, long, double, bool, string?, int, int, int, int, bool) Visible(MediaStatus s) =>
        (s.State, (long)s.Position.TotalSeconds, (long)s.Duration.TotalSeconds, s.Volume, s.IsMuted, s.Error,
            s.AudioTracks?.Count ?? 0, s.AudioTrack, s.Subtitles?.Count ?? 0, s.Subtitle, s.HasVideo);

    private void HandleAudioPreview(AudioPreviewModal modal, InputAction action)
    {
        if (action == InputAction.Back)
        {
            CloseMedia(modal);
            return;
        }
        if (modal.Session is not { } session || modal.DisplayError is not null) return;
        var status = session.Status;
        switch (action)
        {
            case InputAction.Confirm: TogglePlay(session, status); break;
            case InputAction.NavigateLeft: SeekBy(session, status, -MediaSeekStep); break;
            case InputAction.NavigateRight: SeekBy(session, status, MediaSeekStep); break;
            case InputAction.PreviousRegion: SeekBy(session, status, -MediaLongSeekStep); break;
            case InputAction.NextRegion: SeekBy(session, status, MediaLongSeekStep); break;
            case InputAction.NavigateUp: ChangeVolume(session, status, MediaVolumeStep); break;
            case InputAction.NavigateDown: ChangeVolume(session, status, -MediaVolumeStep); break;
            case InputAction.OpenContextMenu: session.SetMuted(!status.IsMuted); break;
            default: return;
        }
        modal.Status = session.Status;
    }

    /// <summary>Tocando ou carregando pausa; no fim, recomeça do início; senão, toca.</summary>
    private static void TogglePlay(IMediaSession session, MediaStatus status)
    {
        switch (status.State)
        {
            case MediaPlaybackState.Playing or MediaPlaybackState.Buffering:
                session.Pause();
                break;
            case MediaPlaybackState.Ended:
                session.Seek(TimeSpan.Zero);
                session.Play();
                break;
            default:
                session.Play();
                break;
        }
    }

    /// <summary>Avança/volta sem passar do início nem do fim.</summary>
    private void SeekBy(IMediaSession session, MediaStatus status, TimeSpan delta)
    {
        if (!status.CanSeek) return;
        var target = status.Position + delta;
        if (target <= TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
            if (status.Position == TimeSpan.Zero) StatusMessage = "Já está no início.";
        }
        else if (target >= status.Duration)
        {
            target = status.Duration;
            if (status.Position >= status.Duration) StatusMessage = "Já está no fim.";
        }
        session.Seek(target);
    }

    private static void ChangeVolume(IMediaSession session, MediaStatus status, double delta)
    {
        session.SetVolume(Math.Clamp(Math.Round(status.Volume + delta, 1), 0, 1));
        if (status.IsMuted && delta > 0) session.SetMuted(false); // aumentar o volume tira o "sem som"
    }

    /// <summary>
    /// Na saída do app: para e libera players e documentos ainda abertos antes de o processo terminar. Sem isso o
    /// player do Windows (e o Direct3D por trás dele) ainda tinha trabalho pendente durante o encerramento e derrubava o
    /// processo no renderizador de software (WARP: VMs, área de trabalho remota, CI).
    /// </summary>
    public void ReleaseMediaForShutdown()
    {
        foreach (var modal in _modals.ToList())
        {
            switch (modal)
            {
                case MediaPreviewModal media:
                    media.IsClosed = true;
                    media.Session?.Dispose();
                    media.Session = null;
                    break;
                case PdfPreviewModal pdf:
                    pdf.Document?.Dispose();
                    break;
            }
        }
    }

    /// <summary>Para o som, libera o arquivo e devolve o foco da lista ao arquivo.</summary>
    private void CloseMedia(MediaPreviewModal modal)
    {
        modal.IsClosed = true;
        modal.Session?.Dispose();
        modal.Session = null;
        CloseModal(modal);
        modal.Pane.List.FocusById(modal.Entry.Id);
    }
}
