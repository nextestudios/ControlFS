using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Mira por giroscópio no teclado virtual (#77, experimental): camada de ponteiro sobre o foco das teclas.</summary>
public class GyroKeyboardJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Gyro_aim_is_off_by_default_then_points_at_keys_without_wrapping_and_keeps_the_dpad() => UiContext.Run(async () =>
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.Search);
        var kb = (await d.WaitKeyboard()).Keyboard;
        kb.FocusKey(1, 0); // "q"

        app.AimKeyboard(3, 0); // desligado por padrão: nada
        Assert.Equal((1, 0), (kb.Row, kb.Column));
        Assert.False(app.Settings.GyroKeyboard);

        app.ToggleGyroKeyboard();
        app.AimKeyboard(0.4, 0); // dentro da mesma tecla: o foco não muda
        Assert.Equal((1, 0), (kb.Row, kb.Column));
        app.AimKeyboard(0.4, 0); // cruzou para a próxima
        Assert.Equal("w", kb.FocusedKey.Label);
        app.AimKeyboard(50, 0); // bem para a direita: para na borda, nunca dá a volta
        Assert.Equal("#", kb.FocusedKey.Label);
        app.AimKeyboard(0, -9);
        Assert.Equal((0, 10), (kb.Row, kb.Column)); // topo à direita ("@")

        d.Press(InputAction.NavigateLeft); // o direcional continua valendo e reancora o ponteiro
        Assert.Equal("0", kb.FocusedKey.Label);
        app.AimKeyboard(0, 1);
        Assert.Equal("p", kb.FocusedKey.Label);

        var view = app.Settings.View;
        d.Press(InputAction.ChangeView); // R3: recentraliza a mira, não troca a exibição
        Assert.Equal(view, app.Settings.View);
        d.Press(InputAction.Confirm);
        Assert.EndsWith("p", kb.Text, StringComparison.Ordinal);
    });
}
