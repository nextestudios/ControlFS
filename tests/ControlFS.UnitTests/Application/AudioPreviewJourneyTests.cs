using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Áudio interno (#60) com um reprodutor falso; o real (MediaPlayer do Windows) está nos WindowsIntegrationTests.</summary>
public class AudioPreviewJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void South_plays_audio_with_controller_play_pause_seek_volume_mute_and_closing_stops_it() => UiContext.Run(async () =>
    {
        File.WriteAllBytes(_tmp.Sub("musica.mp3"), [0x49, 0x44, 0x33, 0x04, 0x00]); // "ID3"
        File.WriteAllBytes(_tmp.Sub("virus.mp3"), [0x4D, 0x5A, 0x90, 0x00]); // executável renomeado
        var player = new FakeMediaPlayer { Duration = TimeSpan.FromSeconds(95) };
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService()) { MediaPlayer = player };
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("musica.mp3");
        d.Press(InputAction.Confirm);
        await d.Idle();

        var audio = Assert.IsType<AudioPreviewModal>(app.TopModal);
        var session = player.Last!;
        Assert.Equal([("musica.mp3", MediaKind.Audio, (string?)null)], player.Opened);
        Assert.Equal(MediaPlaybackState.Playing, audio.Status.State);
        Assert.Contains(app.Hints, h => h.Action == InputAction.Confirm && h.Label == "Pausar");

        d.Press(InputAction.Confirm);
        Assert.Equal(MediaPlaybackState.Paused, session.Status.State);
        Assert.Contains(app.Hints, h => h.Action == InputAction.Confirm && h.Label == "Tocar");
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NextRegion);
        Assert.Equal(TimeSpan.FromSeconds(70), session.Status.Position);
        d.Press(InputAction.NextRegion); // não passa do fim
        Assert.Equal(TimeSpan.FromSeconds(95), session.Status.Position);
        d.Press(InputAction.NavigateLeft);
        Assert.Equal(TimeSpan.FromSeconds(85), session.Status.Position);

        d.Press(InputAction.NavigateDown);
        d.Press(InputAction.NavigateDown);
        Assert.Equal(0.8, session.Status.Volume, 3);
        d.Press(InputAction.OpenContextMenu);
        Assert.True(session.Status.IsMuted);
        Assert.Contains(app.Hints, h => h.Action == InputAction.OpenContextMenu && h.Label == "Com som");
        d.Press(InputAction.NavigateUp); // aumentar o volume tira o "sem som"
        Assert.False(session.Status.IsMuted);

        // O reprodutor avisa por retrato: a cada quadro a tela só é redesenhada quando algo visível mudou.
        var changes = 0;
        app.Changed += () => changes++;
        session.Status = session.Status with { State = MediaPlaybackState.Ended, Position = TimeSpan.FromSeconds(95) };
        app.TickControllers();
        app.TickControllers();
        Assert.Equal(1, changes);
        Assert.Equal(MediaPlaybackState.Ended, audio.Status.State);
        Assert.Contains(app.Hints, h => h.Action == InputAction.Confirm && h.Label == "Tocar de novo");
        d.Press(InputAction.Confirm); // do início
        Assert.Equal((MediaPlaybackState.Playing, TimeSpan.Zero), (session.Status.State, session.Status.Position));

        d.Press(InputAction.Back);
        Assert.True(session.Disposed);
        Assert.Null(app.TopModal);
        Assert.Equal("musica.mp3", app.Browser.List.Focused?.Name);

        // Executável disfarçado: recusado antes do reprodutor.
        await d.FocusItem("virus.mp3");
        d.Press(InputAction.Confirm);
        await d.Idle();
        var refused = Assert.IsType<AudioPreviewModal>(app.TopModal);
        Assert.Equal("O conteúdo é um programa, não áudio ou vídeo: não será reproduzido.", refused.DisplayError);
        Assert.Single(player.Opened);
        Assert.Equal([InputAction.Back], app.Prompts.Select(p => p.Action));
    });
}
