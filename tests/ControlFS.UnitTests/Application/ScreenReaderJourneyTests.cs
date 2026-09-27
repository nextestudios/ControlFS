using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>O que o Narrador ouve ao navegar só com ações semânticas (a janela repassa estes textos à UI Automation).</summary>
public class ScreenReaderJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Focus_moves_and_states_are_announced_once_with_the_context_when_it_changes() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        File.WriteAllText(_tmp.Sub("b.txt"), "b");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);

        Assert.Equal("Início. Pasta de teste, Pasta especial, 1 de 1", app.TakeAnnouncement());
        Assert.Null(app.TakeAnnouncement()); // nada mudou: nada é repetido

        d.Press(InputAction.Confirm);
        await d.Idle();
        var folder = app.TakeAnnouncement()!;
        Assert.StartsWith("Pasta ", folder);
        Assert.EndsWith(". a.txt, arquivo txt, 1 B, 1 de 2", folder);

        // Mesmo contexto: só o item, com os estados por extenso
        d.Press(InputAction.ToggleSelection);
        Assert.Equal("a.txt, arquivo txt, 1 B, marcado, 1 de 2", app.TakeAnnouncement());
        d.Press(InputAction.NavigateDown);
        app.PutOnClipboard(app.Browser, [app.Browser.List.Focused!], FileOperationKind.Move);
        Assert.Equal("b.txt, arquivo txt, 1 B, recortado, 2 de 2", app.TakeAnnouncement());

        // Menu: título ao abrir; item indisponível diz o motivo
        app.PushModal(new MenuModal("Teste", [new MenuItem("Ativo", () => { }), new MenuItem("Travado", () => { }, "Motivo X")]));
        Assert.Equal("Menu Teste. Ativo, 1 de 2", app.TakeAnnouncement());
        d.Press(InputAction.NavigateDown);
        Assert.Equal("Travado, indisponível: Motivo X, 2 de 2", app.TakeAnnouncement());
        d.Press(InputAction.Back);
        Assert.Equal($"{folder[..folder.IndexOf(". ", StringComparison.Ordinal)]}. b.txt, arquivo txt, 1 B, recortado, 2 de 2", app.TakeAnnouncement());

        // Diálogo: título, conteúdo e o botão focado (confirmações começam em Cancelar)
        d.Press(InputAction.Back); // limpa a marcação
        app.TakeAnnouncement();
        app.GoHome();
        app.TakeAnnouncement();
        d.Press(InputAction.Back);
        Assert.Equal("Diálogo Sair do ControlFS? Cancelar, botão 1 de 2", app.TakeAnnouncement());
    });
}
