using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Sistema único dos modais (#172): ícones, ações perigosas, escopo de entrada e foco ao fechar.</summary>
public class ModalSystemJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    /// <summary>Operações de arquivo disponíveis (com Lixeira); nada é executado nestes testes.</summary>
    private sealed class NoOpFileOperations : IFileOperationService
    {
        public bool CanRecycle(string path) => true;
        public FileEntry Rename(string path, string newName) => throw new InvalidOperationException("não usado");
        public Task<OperationResult> RunAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("não usado");
    }

    private Driver Boot()
    {
        Directory.CreateDirectory(_tmp.Sub("Pasta"));
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        File.WriteAllText(_tmp.Sub("b.txt"), "b");
        ZipFixtures.Create(_tmp.Sub("c.zip"), ZipFixtures.Text("dentro.txt", "x"));
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new NoOpFileOperations());
        app.Start();
        return new Driver(app);
    }

    private static void AssertIcons(Modal? modal, string context)
    {
        switch (modal)
        {
            case MenuModal menu:
                Assert.NotEqual(ActionIcon.None, menu.Icon);
                Assert.All(menu.Items, i => Assert.True(i.Icon != ActionIcon.None, $"{context}: \"{i.Label}\" sem ícone"));
                break;
            case DialogModal dialog:
                Assert.All(dialog.Options, o => Assert.True(o.Icon != ActionIcon.None, $"{context}: \"{o.Label}\" sem ícone"));
                break;
            default:
                Assert.Fail($"{context}: esperava um menu ou diálogo, veio {modal?.GetType().Name ?? "nada"}");
                break;
        }
    }

    [Fact]
    public void Every_menu_option_and_dialog_button_has_an_icon_with_a_glyph() => UiContext.Run(async () =>
    {
        foreach (var icon in Enum.GetValues<ActionIcon>().Where(i => i != ActionIcon.None))
            Assert.False(string.IsNullOrEmpty(ActionIcons.Glyph(icon)), $"{icon} sem símbolo");

        var d = Boot();
        d.Press(InputAction.OpenAppMenu);
        AssertIcons(d.App.TopModal, "Menu no início");
        d.Press(InputAction.Back);
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(d.App.TopModal, "Ações de um local");
        d.Press(InputAction.Back);

        d.Press(InputAction.Confirm);
        await d.FocusItem("Pasta");
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(await d.WaitMenu(), "Ações de pasta");
        d.Press(InputAction.Back);
        await d.FocusItem("a.txt");
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(await d.WaitMenu(), "Ações de arquivo");
        await d.ChooseMenu("Excluir…");
        AssertIcons(await d.WaitDialog("Mover 1 item para a Lixeira?"), "Confirmação de exclusão");
        d.Press(InputAction.Back);
        await d.FocusItem("c.zip");
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(await d.WaitMenu(), "Ações de compactado");
        await d.ChooseMenu("Extrair aqui");
        AssertIcons(await d.WaitDialog("Extrair"), "Resumo da extração");
        d.Press(InputAction.Back);
        await d.FocusItem("b.txt");
        d.Press(InputAction.ToggleSelection);
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(await d.WaitMenu(), "Itens marcados");
        await d.ChooseMenu("Compactar");
        AssertIcons(await d.WaitDialog("Compactar"), "Compactar");
        d.Press(InputAction.Back);
        d.Press(InputAction.OpenAppMenu);
        AssertIcons(d.App.TopModal, "Menu na pasta");
        d.Press(InputAction.Back);
        d.Press(InputAction.NextRegion); // faixa de abas
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(d.App.TopModal, "Abas");
    });

    [Fact]
    public void Destructive_actions_are_flagged_and_never_the_initial_focus() => UiContext.Run(async () =>
    {
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.txt");
        d.Press(InputAction.OpenContextMenu);
        var menu = await d.WaitMenu();
        Assert.True(menu.Items.Single(i => i.Label == "Excluir…").IsDestructive);
        Assert.All(menu.Items.Where(i => i.Label != "Excluir…"), i => Assert.False(i.IsDestructive, i.Label));
        Assert.False(menu.Items[menu.FocusIndex].IsDestructive);
        d.Press(InputAction.Back);

        // Quem monta um menu ou diálogo não consegue abrir com o foco numa ação perigosa.
        var app = d.App;
        app.PushModal(new MenuModal("Teste", [new MenuItem("Excluir…", () => { }, Icon: ActionIcon.Delete), new MenuItem("Copiar", () => { }, Icon: ActionIcon.Copy)]));
        Assert.Equal("Copiar", ((MenuModal)app.TopModal!).Items[((MenuModal)app.TopModal!).FocusIndex].Label);
        d.Press(InputAction.Back);
        var dialog = new DialogModal("Teste?", [], sensitive: true);
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => app.CloseModal(dialog));
        dialog.Options.Add(new DialogOption("Excluir permanentemente", DialogOptionKind.Danger, () => { }));
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        app.PushModal(dialog);
        Assert.Equal("Cancelar", dialog.Options[dialog.FocusIndex].Label);
        Assert.Equal(ActionIcon.Warning, dialog.Icon);
        app.TakeAnnouncement();
        d.Press(InputAction.NavigateUp);
        Assert.Contains("ação perigosa", app.TakeAnnouncement(), StringComparison.Ordinal);
    });

    [Fact]
    public void Input_under_an_active_modal_never_reaches_the_screen_below() => UiContext.Run(async () =>
    {
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.txt");
        var app = d.App;
        var location = app.Browser.Location;
        var grid = app.IsGrid;
        d.Press(InputAction.OpenAppMenu);
        var menu = await d.WaitMenu();

        foreach (var action in new[] { InputAction.NavigateDown, InputAction.PageDown, InputAction.ToggleSelection, InputAction.ChangeView, InputAction.Search, InputAction.PreviousRegion, InputAction.NextRegion })
            d.Press(action);
        app.PointerActivateListItem(0); // clique numa linha atrás do modal
        app.PointerActivateTab(0);

        Assert.Same(menu, app.TopModal);
        Assert.Equal("a.txt", app.Browser.List.Focused?.Name);
        Assert.Equal(0, app.Browser.List.SelectionCount);
        Assert.Equal(PaneRegion.List, app.FocusRegion);
        Assert.Same(location, app.Browser.Location);
        Assert.Equal(grid, app.IsGrid);
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
    });

    [Fact]
    public void Closing_nested_modals_restores_focus_step_by_step() => UiContext.Run(async () =>
    {
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("b.txt");
        var app = d.App;
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Compactar…");
        var compress = await d.WaitDialog("Compactar");
        d.ChooseOption(compress, "Nome:"); // teclado por cima do diálogo
        await d.WaitKeyboard();
        var nameOption = compress.FocusIndex;

        d.Press(InputAction.Back); // fecha só o teclado
        Assert.Same(compress, app.TopModal);
        Assert.Equal(nameOption, compress.FocusIndex);
        d.Press(InputAction.Back); // Cancelar
        Assert.Null(app.TopModal);
        Assert.Equal("b.txt", app.Browser.List.Focused?.Name);
        Assert.Equal(PaneRegion.List, app.FocusRegion);
    });
}
