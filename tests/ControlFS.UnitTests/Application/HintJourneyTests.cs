using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Input;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Application;

/// <summary>O rodapé por contexto: só o que funciona, com o rótulo do que cada botão fará.</summary>
public class HintJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static string? Label(AppController app, InputAction action) => app.Hints.SingleOrDefault(h => h.Action == action)?.Label;

    private async Task<Driver> OpenTempFolder()
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        return d;
    }

    [Fact]
    public void Archive_file_offers_explore_mark_and_extract_and_the_actions_menu_opens_on_extract() => UiContext.Run(async () =>
    {
        Create(_tmp.Sub("fotos.zip"), Text("a.txt", "a"));
        var d = await OpenTempFolder();
        await d.FocusItem("fotos.zip");

        Assert.Equal("Explorar", Label(d.App, InputAction.Confirm));
        Assert.Equal("Marcar", Label(d.App, InputAction.ToggleSelection)); // compactados continuam marcáveis para operações em lote
        Assert.Equal("Extrair…", Label(d.App, InputAction.OpenContextMenu));

        d.Press(InputAction.OpenContextMenu);
        var menu = await d.WaitMenu();
        Assert.Equal("Extrair para \"fotos\"", menu.Items[menu.FocusIndex].Label);
        d.Press(InputAction.Back);

        d.Press(InputAction.Confirm); // Explorar: entra no compactado
        await d.Idle();
        Assert.IsType<ControlFS.Core.Models.ArchiveLocation>(d.App.ActivePane.Location);
        Assert.Equal("Detalhes", Label(d.App, InputAction.Confirm));
        Assert.Equal("Extrair…", Label(d.App, InputAction.OpenContextMenu));
    });

    [Fact]
    public void Multi_selection_shows_unmark_operations_and_cancel() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        File.WriteAllText(_tmp.Sub("b.txt"), "b");
        var d = await OpenTempFolder();
        await d.FocusItem("a.txt");
        d.Press(InputAction.ToggleSelection);
        await d.FocusItem("b.txt");
        d.Press(InputAction.ToggleSelection);

        Assert.Equal("Desmarcar", Label(d.App, InputAction.ToggleSelection));
        Assert.Equal("Operações (2)", Label(d.App, InputAction.OpenContextMenu));
        Assert.Equal("Cancelar seleção", Label(d.App, InputAction.Back));
        d.Press(InputAction.OpenContextMenu);
        Assert.Equal("2 item(ns) marcado(s)", (await d.WaitMenu()).Title);
    });

    [Fact]
    public void On_screen_keyboard_shows_select_delete_done_cancel_and_hides_select_for_the_physical_keyboard() => UiContext.Run(async () =>
    {
        var d = await OpenTempFolder();
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Nova pasta");
        await d.WaitKeyboard();

        d.App.SetActiveController(ControllerFamily.Xbox);
        Assert.Equal("Selecionar", Label(d.App, InputAction.Confirm));
        Assert.Equal("Apagar", Label(d.App, InputAction.ToggleSelection));
        Assert.Equal("Concluir", Label(d.App, InputAction.OpenAppMenu)); // Start/Options
        Assert.Equal("Cancelar", Label(d.App, InputAction.Back));

        d.App.SetActiveController(null); // teclado físico digita direto; Enter conclui
        Assert.Null(Label(d.App, InputAction.Confirm));
        Assert.Equal("Enter", Assert.Single(d.App.Prompts, p => p.Action == InputAction.OpenAppMenu).Key);
    });

    [Fact]
    public void Dialogs_and_menus_name_the_focused_choice_and_hide_confirm_on_unavailable_items() => UiContext.Run(async () =>
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);

        d.Press(InputAction.OpenAppMenu); // na tela inicial, o primeiro item (Colar) está indisponível
        var menu = await d.WaitMenu();
        Assert.False(menu.Items[menu.FocusIndex].IsEnabled);
        Assert.Null(Label(app, InputAction.Confirm));
        while (!menu.Items[menu.FocusIndex].IsEnabled) d.Press(InputAction.NavigateDown);
        Assert.Equal("Escolher", Label(app, InputAction.Confirm));
        d.Press(InputAction.Back);

        d.Press(InputAction.Back); // confirmação de saída
        var exit = await d.WaitDialog("Sair do ControlFS?");
        Assert.Equal("Cancelar", Label(app, InputAction.Confirm));
        d.Press(InputAction.NavigateRight);
        Assert.Equal("Sair", Label(app, InputAction.Confirm));
        Assert.Equal("Cancelar", Label(app, InputAction.Back));
        Assert.Same(exit, app.TopModal);
    });
}
