using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Tela cheia (#230): escolha salva pelo menu e pelas Configurações; o vídeo (#170) não a muda. O presenter do Windows é manual.</summary>
public class FullScreenJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    [Fact]
    public void Full_screen_is_toggled_from_the_menu_tile_and_settings_persists_and_video_restores_the_choice() => UiContext.Run(async () =>
    {
        File.WriteAllBytes(_tmp.Sub("filme.mp4"), [0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70]);
        var store = new JsonSettingsStore(_data.Path);
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store) { MediaPlayer = new FakeMediaPlayer { Duration = TimeSpan.FromMinutes(1) } };
        app.Start();
        var d = new Driver(app);
        Assert.False(app.WindowFullScreen);

        // Bloco do menu do app: liga, e o bloco passa a oferecer a volta à janela.
        d.Press(InputAction.OpenAppMenu);
        var menu = Assert.IsType<MenuModal>(app.TopModal);
        Assert.Contains(menu.Items.Take(menu.QuickCount), i => i.Label == "Tela cheia" && i.Icon == ActionIcon.FullScreen);
        Assert.Equal((3, 3), (menu.QuickColumns, menu.QuickRows)); // nove blocos: 3×3, não 4+4+1
        await d.ChooseMenu("Tela cheia");
        Assert.True(app.Settings.FullScreen);
        Assert.True(app.WindowFullScreen);
        d.Press(InputAction.OpenAppMenu);
        menu = Assert.IsType<MenuModal>(app.TopModal);
        Assert.Contains(menu.Items.Take(menu.QuickCount), i => i.Label == "Sair da tela cheia");
        d.Press(InputAction.Back);

        // Fica salva: a próxima abertura já começa em tela cheia.
        var relaunched = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store);
        relaunched.Start();
        Assert.True(relaunched.WindowFullScreen);

        // Configurações desliga e o menu continua aberto com o texto novo.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Configurações");
        await d.ChooseMenu("Tela cheia: ligado");
        Assert.False(app.Settings.FullScreen);
        Assert.Contains(Assert.IsType<MenuModal>(app.TopModal).Items, i => i.Label == "Tela cheia: desligado");
        while (app.TopModal is not null) d.Press(InputAction.Back);

        // Vídeo: a janela entra em tela cheia enquanto ele está aberto; F11 durante o vídeo sai só para ele, sem mudar a escolha.
        d.Press(InputAction.Confirm);
        await d.FocusItem("filme.mp4");
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.IsType<VideoPlayerModal>(app.TopModal);
        Assert.True(app.WindowFullScreen);
        app.ToggleFullScreen();
        Assert.False(app.WindowFullScreen);
        Assert.False(app.Settings.FullScreen);
        for (var i = 0; i < 3 && app.TopModal is VideoPlayerModal; i++) d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
        Assert.False(app.WindowFullScreen); // volta à janela comum, a escolha salva

        // Com a tela cheia escolhida, fechar o vídeo mantém a tela cheia.
        app.ToggleFullScreen();
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.IsType<VideoPlayerModal>(app.TopModal);
        Assert.True(app.WindowFullScreen);
        for (var i = 0; i < 3 && app.TopModal is VideoPlayerModal; i++) d.Press(InputAction.Back);
        Assert.True(app.WindowFullScreen);
        Assert.True(app.Settings.FullScreen);
    });
}
