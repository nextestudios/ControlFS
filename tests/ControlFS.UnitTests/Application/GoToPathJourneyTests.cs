using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class GoToPathJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Typing_a_missing_path_keeps_the_keyboard_with_an_error_and_an_existing_one_navigates_there() => UiContext.Run(async () =>
    {
        var roms = _tmp.MakeDir("Jogos", "ROMs");
        File.WriteAllText(Path.Join(roms, "leia-me.txt"), "x");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);

        // Início → Menu → Ir para caminho…: o teclado de caminho abre vazio
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Ir para caminho");
        var keyboard = await d.WaitKeyboard();
        Assert.Equal(ControlFS.Core.Text.TextFieldKind.Path, keyboard.Keyboard.Kind);

        // Caminho inexistente: erro claro, teclado continua aberto com o texto
        var missing = Path.Join(_tmp.Path, "NaoExiste");
        app.TypeText(missing);
        d.Press(InputAction.OpenAppMenu); // Concluir
        keyboard = await d.WaitKeyboard();
        Assert.StartsWith("Pasta não encontrada", keyboard.Keyboard.ErrorMessage);
        Assert.Equal(missing, keyboard.Keyboard.Text);
        Assert.Equal(Screen.Home, app.Screen);

        // Caminho existente: navega até lá
        app.TypeSelectAll();
        app.TypeText(roms);
        d.Press(InputAction.OpenAppMenu);
        await d.Idle();
        Assert.Null(app.TopModal);
        Assert.Equal(Screen.Browser, app.Screen);
        Assert.Equal(roms, ((PhysicalLocation)app.Browser.Location!).FullPath);

        // Caminho de um arquivo: abre a pasta dele com o foco no arquivo (o teclado já vem com a pasta atual)
        d.Press(InputAction.NavigateLeft);
        await d.Idle();
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Ir para caminho");
        keyboard = await d.WaitKeyboard();
        Assert.Equal(Path.GetDirectoryName(roms), keyboard.Keyboard.Text);
        app.TypeText(Path.Join(roms, "leia-me.txt"));
        d.Press(InputAction.OpenAppMenu);
        await d.Idle();
        Assert.Equal(roms, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal("leia-me.txt", app.Browser.List.Focused?.Name);
    });
}
