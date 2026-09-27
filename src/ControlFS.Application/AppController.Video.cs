using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Preview;

namespace ControlFS.Application;

/// <summary>
/// Reprodutor de vídeo em tela cheia (#61, #170) com os codecs do Windows: sobreposição que some tocando, busca com destino
/// mostrado antes de aplicar, volume, faixas de áudio e legendas quando o vídeo as tem, e "continuar de onde parou" por
/// arquivo (resumo do caminho + tamanho + data, só neste computador).
/// </summary>
public sealed partial class AppController
{
    /// <summary>Tempo sem entrada, tocando, até a sobreposição sumir.</summary>
    public static readonly TimeSpan VideoOverlayHideAfter = TimeSpan.FromSeconds(3);

    /// <summary>Tempo sem novos toques de busca até aplicar o destino mostrado.</summary>
    public static readonly TimeSpan VideoSeekCommitAfter = TimeSpan.FromMilliseconds(700);

    /// <summary>LT/RT andam esta fração do vídeo (linha do tempo).</summary>
    public const double VideoScrubFraction = 0.05;

    private List<PlaybackPosition>? _positions;
    private readonly object _positionsSave = new();

    /// <summary>Onde os vídeos pararam (#170); null: não lembra (nada é gravado).</summary>
    public IPlaybackPositionStore? PlaybackPositions { get; init; }

    internal static bool IsPlayableVideo(FileEntry entry) =>
        entry is { Kind: EntryKind.File, FullPath: not null, IsBlocked: false } && MediaPreviewPolicy.IsVideoExtension(entry.Extension);

    private List<PlaybackPosition> Positions => _positions ??= [.. PlaybackPositions?.Load() ?? []];

    /// <summary>Abre o vídeo; parcialmente assistido pergunta antes: continuar ou começar do início.</summary>
    internal void OpenVideo(PaneState pane, FileEntry entry)
    {
        if (MediaPlayer is null || entry.FullPath is null) return;
        Track(OpenVideoAsync(pane, entry, entry.FullPath));
    }

    private async Task OpenVideoAsync(PaneState pane, FileEntry entry, string path)
    {
        string key;
        string? subtitle;
        try
        {
            (key, subtitle) = await Task.Run(() =>
            {
                var info = new FileInfo(path);
                return (PlaybackResume.Key(info.FullName, info.Length, info.LastWriteTimeUtc), FindSubtitle(path));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("Não foi possível abrir o vídeo", [("Motivo", ex.Message)], icon: ActionIcon.Error);
            return;
        }
        var saved = Positions.FirstOrDefault(p => p.Key == key);
        if (saved is null || !PlaybackResume.IsPartial(saved.Position, TimeSpan.Zero))
        {
            StartVideo(pane, entry, key, null, subtitle);
            return;
        }
        var at = MediaPreviewPolicy.FormatTime(saved.Position);
        var dialog = new DialogModal("Continuar o vídeo?", [("Arquivo", entry.Name), ("Parou em", at)]) { Icon = ActionIcon.Video };
        var resume = new DialogOption($"Continuar de {at}", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            StartVideo(pane, entry, key, saved.Position, subtitle);
        }, icon: ActionIcon.Resume);
        var restart = new DialogOption("Começar do início", DialogOptionKind.Safe, () =>
        {
            CloseModal(dialog);
            ForgetPosition(key);
            StartVideo(pane, entry, key, null, subtitle);
        }, icon: ActionIcon.Retry);
        var cancel = new DialogOption("Voltar à lista", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Back);
        dialog.Options.AddRange([resume, restart, cancel]);
        dialog.BackOption = cancel;
        PushModal(dialog);
    }

    /// <summary>Legenda ao lado do vídeo com o mesmo nome (<c>filme.srt</c>, <c>filme.vtt</c>), só arquivos locais comuns.</summary>
    private static string? FindSubtitle(string videoPath)
    {
        var stem = Path.Join(Path.GetDirectoryName(videoPath), Path.GetFileNameWithoutExtension(videoPath));
        foreach (var extension in (string[])[".srt", ".vtt"])
        {
            var candidate = stem + extension;
            if (File.Exists(candidate) && new FileInfo(candidate) is { Length: > 0 and < 5 * 1024 * 1024 } file && !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return candidate;
        }
        return null;
    }

    private void StartVideo(PaneState pane, FileEntry entry, string key, TimeSpan? resumeAt, string? subtitle)
    {
        var modal = new VideoPlayerModal(pane, entry, key, resumeAt, subtitle) { LastInput = Clock() };
        PushModal(modal);
        Track(StartMediaAsync(modal, MediaKind.Video, subtitle));
    }

    /// <summary>Chamado a cada quadro com um vídeo aberto: retomada pendente, busca a aplicar e sobreposição que some.</summary>
    private void TickVideo(VideoPlayerModal modal)
    {
        var now = Clock();
        var changed = false;
        if (modal is { ResumeAt: { } resume, Session: { } session } && modal.Status.CanSeek)
        {
            modal.ResumeAt = null;
            session.Seek(resume < modal.Status.Duration ? resume : TimeSpan.Zero);
            modal.Status = session.Status;
            changed = true;
        }
        if (modal.SeekTarget is { } target && now - modal.LastSeekInput >= VideoSeekCommitAfter)
        {
            modal.SeekTarget = null;
            if (modal.Session is { } s)
            {
                s.Seek(target);
                modal.Status = s.Status;
            }
            modal.LastInput = now; // a sobreposição fica um pouco depois do salto
            changed = true;
        }
        if (modal.OverlayVisible && modal.SeekTarget is null && modal.Status.State == MediaPlaybackState.Playing
            && ReferenceEquals(TopModal, modal) && now - modal.LastInput >= VideoOverlayHideAfter)
        {
            modal.OverlayVisible = false;
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    private void HandleVideo(VideoPlayerModal modal, InputAction action)
    {
        var now = Clock();
        if (action == InputAction.Back)
        {
            // Voltar tira a sobreposição primeiro (só tocando; pausado ela fica); depois volta à lista.
            if (modal.SeekTarget is not null)
            {
                modal.SeekTarget = null;
                return;
            }
            if (modal.OverlayVisible && modal.Status.State == MediaPlaybackState.Playing && modal.DisplayError is null)
            {
                modal.OverlayVisible = false;
                return;
            }
            CloseVideo(modal);
            return;
        }
        modal.OverlayVisible = true;
        modal.LastInput = now;
        if (modal.Session is not { } session || modal.DisplayError is not null) return;
        var status = session.Status;
        switch (action)
        {
            case InputAction.Confirm:
                if (modal.SeekTarget is { } target)
                {
                    modal.SeekTarget = null; // Confirmar aplica o destino na hora
                    session.Seek(target);
                }
                else
                {
                    TogglePlay(session, status);
                }
                break;
            case InputAction.NavigateLeft: PrepareSeek(modal, status, -MediaSeekStep, now); break;
            case InputAction.NavigateRight: PrepareSeek(modal, status, MediaSeekStep, now); break;
            case InputAction.PreviousRegion: PrepareSeek(modal, status, -MediaLongSeekStep, now); break;
            case InputAction.NextRegion: PrepareSeek(modal, status, MediaLongSeekStep, now); break;
            case InputAction.PageUp: PrepareSeek(modal, status, -status.Duration * VideoScrubFraction, now); break;
            case InputAction.PageDown: PrepareSeek(modal, status, status.Duration * VideoScrubFraction, now); break;
            case InputAction.NavigateUp: ChangeVolume(session, status, MediaVolumeStep); break;
            case InputAction.NavigateDown: ChangeVolume(session, status, -MediaVolumeStep); break;
            case InputAction.OpenContextMenu: ShowVideoOptions(modal); break;
            default: return;
        }
        modal.Status = session.Status;
    }

    /// <summary>Acumula o destino (a partir do destino em preparo ou da posição atual), sem passar do início nem do fim.</summary>
    private void PrepareSeek(VideoPlayerModal modal, MediaStatus status, TimeSpan delta, TimeSpan now)
    {
        if (!status.CanSeek)
        {
            StatusMessage = "Este vídeo ainda não permite avançar ou voltar.";
            return;
        }
        var from = modal.SeekTarget ?? status.Position;
        var target = from + delta;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (target > status.Duration) target = status.Duration;
        if (target == from && modal.SeekTarget is not null) StatusMessage = delta < TimeSpan.Zero ? "Já está no início." : "Já está no fim.";
        modal.SeekTarget = target;
        modal.LastSeekInput = now;
    }

    /// <summary>Opções (Norte): legendas, faixa de áudio, som, recomeçar e esquecer a posição. O indisponível diz o motivo.</summary>
    private void ShowVideoOptions(VideoPlayerModal modal)
    {
        if (modal.Session is not { } session) return;
        var status = session.Status;
        var items = new List<MenuItem>();
        var subtitles = status.Subtitles ?? [];
        if (subtitles.Count == 0)
        {
            items.Add(new MenuItem("Legendas", null, "Este vídeo não tem legendas embutidas nem um .srt/.vtt com o mesmo nome ao lado.", Icon: ActionIcon.Subtitles, Section: "Legendas"));
        }
        else
        {
            items.Add(new MenuItem(status.Subtitle < 0 ? "✓ Legendas desligadas" : "Desligar legendas", () => session.SelectSubtitle(-1), Icon: ActionIcon.Subtitles, Section: "Legendas"));
            foreach (var track in subtitles)
                items.Add(new MenuItem((track.Index == status.Subtitle ? "✓ " : string.Empty) + track.Label, () => session.SelectSubtitle(track.Index), Icon: ActionIcon.Subtitles, Section: "Legendas"));
        }
        var audio = status.AudioTracks ?? [];
        if (audio.Count > 1)
            foreach (var track in audio)
                items.Add(new MenuItem((track.Index == status.AudioTrack ? "✓ " : string.Empty) + track.Label, () => session.SelectAudioTrack(track.Index), Icon: ActionIcon.Audio, Section: "Faixa de áudio"));
        else
            items.Add(new MenuItem("Faixa de áudio", null, audio.Count == 1 ? "Este vídeo tem uma faixa de áudio só." : "Este vídeo não tem faixas de áudio para escolher.", Icon: ActionIcon.Audio, Section: "Faixa de áudio"));
        items.Add(new MenuItem(status.IsMuted ? "Com som" : "Sem som", () => session.SetMuted(!status.IsMuted), Icon: ActionIcon.Audio, Section: "Reprodução"));
        items.Add(new MenuItem("Recomeçar do início", () =>
        {
            session.Seek(TimeSpan.Zero);
            session.Play();
        }, status.CanSeek ? null : "Este vídeo ainda não permite voltar.", Icon: ActionIcon.Retry, Section: "Reprodução"));
        items.Add(new MenuItem("Esquecer onde parei", () =>
        {
            ForgetPosition(modal.ResumeKey);
            modal.ForgetOnClose = true;
            StatusMessage = "Posição esquecida: da próxima vez o vídeo começa do início.";
        }, PlaybackPositions is null ? "Posições não são lembradas nesta compilação." : null, Icon: ActionIcon.Erase, Section: "Reprodução"));
        PushModal(new MenuModal(modal.Entry.Name, items) { Icon = ActionIcon.Video });
    }

    /// <summary>Guarda onde parou (parcialmente assistido) ou esquece (começo/fim), para, libera e devolve o foco ao arquivo.</summary>
    private void CloseVideo(VideoPlayerModal modal)
    {
        var status = modal.Session?.Status ?? modal.Status;
        if (modal.ForgetOnClose || status.State == MediaPlaybackState.Ended || !PlaybackResume.IsPartial(status.Position, status.Duration))
            ForgetPosition(modal.ResumeKey);
        else if (modal.Session is not null && status.State != MediaPlaybackState.Failed)
            RememberPosition(modal.ResumeKey, status.Position);
        CloseMedia(modal);
    }

    private void RememberPosition(string key, TimeSpan position)
    {
        if (PlaybackPositions is null) return;
        var positions = Positions;
        positions.RemoveAll(p => p.Key == key);
        positions.Insert(0, new PlaybackPosition(key, position, DateTimeOffset.UtcNow));
        if (positions.Count > PlaybackResume.MaxEntries) positions.RemoveRange(PlaybackResume.MaxEntries, positions.Count - PlaybackResume.MaxEntries);
        SavePositions();
    }

    private void ForgetPosition(string key)
    {
        if (PlaybackPositions is null || Positions.RemoveAll(p => p.Key == key) == 0) return;
        SavePositions();
    }

    /// <summary>Configurações → apagar todas as posições salvas.</summary>
    internal void ForgetAllPositions()
    {
        if (PlaybackPositions is null) return;
        Positions.Clear();
        SavePositions();
        StatusMessage = "Posições dos vídeos apagadas.";
    }

    private void SavePositions()
    {
        var store = PlaybackPositions!;
        var snapshot = Positions.ToList();
        Track(Task.Run(() =>
        {
            try
            {
                lock (_positionsSave) store.Save(snapshot); // gravações em ordem, nunca duas no mesmo temporário
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Post(() => StatusMessage = "Não foi possível salvar onde o vídeo parou: " + ex.Message);
            }
        }));
    }
}
