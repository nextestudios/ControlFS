using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Reprodutor de vídeo (#61, #170) com reprodutor e posições falsos; o real usa o MediaPlayer do Windows.</summary>
public class VideoPlayerJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private TimeSpan _now;

    public void Dispose() => _tmp.Dispose();

    private sealed class MemoryPositions : IPlaybackPositionStore
    {
        public List<PlaybackPosition> Saved { get; private set; } = [];

        public IReadOnlyList<PlaybackPosition> Load() => Saved;

        public void Save(IReadOnlyList<PlaybackPosition> positions) => Saved = [.. positions];
    }

    private (AppController App, Driver Driver, FakeMediaPlayer Player, MemoryPositions Positions) Boot()
    {
        File.WriteAllBytes(_tmp.Sub("filme.mp4"), [0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70]); // "ftyp"
        File.WriteAllText(_tmp.Sub("filme.srt"), "1\r\n00:00:01,000 --> 00:00:02,000\r\nOlá\r\n");
        File.WriteAllBytes(_tmp.Sub("outro.mp4"), [0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70]);
        var player = new FakeMediaPlayer { Duration = TimeSpan.FromMinutes(10) };
        var positions = new MemoryPositions();
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService()) { MediaPlayer = player, PlaybackPositions = positions, Clock = () => _now };
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        return (app, d, player, positions);
    }

    [Fact]
    public void Video_plays_full_screen_with_an_overlay_that_hides_previewed_bounded_seeks_and_back_hides_the_overlay_first() => UiContext.Run(async () =>
    {
        var (app, d, player, _) = Boot();
        await d.FocusItem("filme.mp4");
        d.Press(InputAction.Confirm);
        await d.Idle();
        var video = Assert.IsType<VideoPlayerModal>(app.TopModal);
        var session = player.Last!;
        Assert.Equal(("filme.mp4", MediaKind.Video, (string?)"filme.srt"), player.Opened[0]); // legenda com o mesmo nome
        Assert.True(video.OverlayVisible);

        // Tocando, a sobreposição some sozinha; qualquer entrada a traz de volta.
        _now += AppController.VideoOverlayHideAfter;
        app.TickControllers();
        Assert.False(video.OverlayVisible);
        d.Press(InputAction.NavigateUp);
        Assert.True(video.OverlayVisible);

        // Busca com destino mostrado antes: três toques em Direita = +30 s, aplicados quando os toques param.
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateRight);
        Assert.Equal(TimeSpan.FromSeconds(30), video.SeekTarget);
        Assert.Equal(TimeSpan.Zero, session.Status.Position);
        Assert.Contains(app.Hints, h => h.Action == InputAction.Confirm && h.Label == "Ir agora");
        _now += AppController.VideoSeekCommitAfter;
        app.TickControllers();
        Assert.Null(video.SeekTarget);
        Assert.Equal(TimeSpan.FromSeconds(30), session.Status.Position);

        // Linha do tempo (RT) e limites: nunca antes do início nem depois do fim; Sul aplica na hora.
        d.Press(InputAction.PageDown);
        Assert.Equal(TimeSpan.FromSeconds(60), video.SeekTarget); // 30 s + 5% de 10 min
        for (var i = 0; i < 30; i++) d.Press(InputAction.NextRegion);
        Assert.Equal(TimeSpan.FromMinutes(10), video.SeekTarget);
        d.Press(InputAction.Confirm);
        Assert.Equal(TimeSpan.FromMinutes(10), session.Status.Position);
        for (var i = 0; i < 12; i++) d.Press(InputAction.PreviousRegion);
        Assert.Equal(TimeSpan.Zero, video.SeekTarget);
        d.Press(InputAction.Back); // Voltar cancela o salto em preparo
        Assert.Null(video.SeekTarget);
        Assert.Same(video, app.TopModal);

        // Voltar: primeiro esconde a sobreposição (tocando), depois volta à lista com o foco no vídeo.
        Assert.Equal(MediaPlaybackState.Playing, session.Status.State);
        d.Press(InputAction.Back);
        Assert.False(video.OverlayVisible);
        Assert.Same(video, app.TopModal);
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
        Assert.True(session.Disposed);
        Assert.Equal("filme.mp4", app.Browser.List.Focused?.Name);
    });

    [Fact]
    public void Partly_watched_video_offers_resume_or_start_over_per_file_and_the_options_hide_or_explain_what_is_missing() => UiContext.Run(async () =>
    {
        var (app, d, player, positions) = Boot();
        await d.FocusItem("filme.mp4");
        d.Press(InputAction.Confirm);
        await d.Idle();
        var session = player.Last!;
        session.Seek(TimeSpan.FromMinutes(4));

        // Norte: legendas e áudio. Sem faixas de áudio para escolher, o item explica; a legenda externa pode ser ligada.
        session.Status = session.Status with { Subtitles = [new MediaTrack(0, "filme.srt")] };
        d.Press(InputAction.OpenContextMenu);
        var menu = await d.WaitMenu();
        Assert.Contains(menu.Items, i => i.Label == "Faixa de áudio" && !i.IsEnabled && i.DisabledReason!.Contains("não tem faixas", StringComparison.Ordinal));
        await d.ChooseMenu("filme.srt");
        Assert.Equal(0, session.Status.Subtitle);

        d.Press(InputAction.Back); // tocando: esconde a sobreposição
        d.Press(InputAction.Back); // fecha e guarda onde parou
        await d.Idle();
        Assert.Equal(TimeSpan.FromMinutes(4), Assert.Single(positions.Saved).Position);
        Assert.Equal(64, positions.Saved[0].Key.Length); // resumo, nunca o caminho

        // Outro arquivo não herda a posição.
        await d.FocusItem("outro.mp4");
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.IsType<VideoPlayerModal>(app.TopModal);
        d.Press(InputAction.Back);
        d.Press(InputAction.Back);
        await d.Idle();

        // O mesmo arquivo pergunta: Continuar retoma assim que o vídeo permite buscar.
        await d.FocusItem("filme.mp4");
        d.Press(InputAction.Confirm);
        var dialog = await d.WaitDialog("Continuar o vídeo?");
        Assert.Equal("Continuar de 4:00", dialog.Options[dialog.FocusIndex].Label);
        d.Press(InputAction.Confirm);
        await d.Idle();
        app.TickControllers();
        var resumed = player.Last!;
        Assert.Equal(TimeSpan.FromMinutes(4), resumed.Status.Position);

        // Terminar o vídeo esquece a posição; da próxima vez começa do início sem perguntar.
        resumed.Status = resumed.Status with { State = MediaPlaybackState.Ended, Position = TimeSpan.FromMinutes(10) };
        app.TickControllers();
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Empty(positions.Saved);
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.IsType<VideoPlayerModal>(app.TopModal);
    });

    [Fact]
    public void Unsupported_codec_shows_a_clear_message_and_back_returns_to_the_list() => UiContext.Run(async () =>
    {
        var (app, d, player, positions) = Boot();
        await d.FocusItem("outro.mp4");
        d.Press(InputAction.Confirm);
        await d.Idle();
        var video = Assert.IsType<VideoPlayerModal>(app.TopModal);
        player.Last!.Status = player.Last.Status with { State = MediaPlaybackState.Failed, Error = "Este formato ou codec não é suportado neste Windows." };
        app.TickControllers();
        Assert.Equal("Este formato ou codec não é suportado neste Windows.", video.DisplayError);
        Assert.Equal([InputAction.Back], app.Prompts.Select(p => p.Action));
        d.Press(InputAction.Confirm); // nada acontece, nada trava
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
        Assert.Empty(positions.Saved);
    });
}
