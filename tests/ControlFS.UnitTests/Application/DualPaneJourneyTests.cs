using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Application;

/// <summary>
/// Jornada C da especificação (#56): dois painéis, cada um com local, marcação e foco próprios; L3 troca o painel ativo
/// sem mexer em nada; copiar, mover e extrair para o outro painel mostram origem e destino antes de executar.
/// </summary>
public class DualPaneJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static PhysicalLocation Where(PaneState pane) => Assert.IsType<PhysicalLocation>(pane.Location);

    [Fact]
    public void Copy_move_and_extract_to_the_other_pane_show_source_and_destination_and_switching_panes_changes_nothing() => UiContext.Run(async () =>
    {
        var source = _tmp.MakeDir("Origem");
        var target = _tmp.MakeDir("Destino");
        File.WriteAllText(Path.Join(source, "a.txt"), "a");
        File.WriteAllText(Path.Join(source, "b.txt"), "b");
        Create(Path.Join(source, "pack.zip"), Text("leia-me.txt", "olá"));
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();

        // Menu → Dois painéis: o direito abre na mesma pasta; o foco continua no esquerdo.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Dois painéis: desligado");
        await d.Idle();
        Assert.True(app.DualPaneActive);
        Assert.Same(app.LeftPane, app.Browser);
        Assert.Equal(_tmp.Path, Where(app.SecondPane).FullPath);
        Assert.Contains(app.Hints, h => h.Action == InputAction.SwitchPane && h.Label == "Painel direito");

        // Esquerdo em Origem com a.txt marcado; L3 passa ao direito sem desmarcar nem iniciar nada.
        await d.FocusItem("Origem");
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.txt");
        d.Press(InputAction.ToggleSelection);
        d.Press(InputAction.SwitchPane);
        Assert.Same(app.SecondPane, app.Browser);
        Assert.Equal(["a.txt"], app.LeftPane.List.SelectedEntries.Select(e => e.Name));
        Assert.Equal("a.txt", app.LeftPane.List.Focused?.Name);
        Assert.Empty(app.Operations.Items);
        await d.FocusItem("Destino");
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.SwitchPane);
        Assert.Same(app.LeftPane, app.Browser);
        Assert.Equal(target, Where(app.SecondPane).FullPath);

        // Copiar o marcado para o outro painel: o resumo diz de onde e para onde.
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Copiar para o outro painel");
        var copy = await d.WaitDialog("Copiar 1 item?");
        Assert.Contains(("De", source), copy.Lines);
        Assert.Contains(("Para", target), copy.Lines);
        d.ChooseOption(copy, "Copiar");
        await d.WaitDialog("Copiar: concluído");
        d.Press(InputAction.Back); // fecha o resultado
        await UiContext.WaitUntil(() => app.SecondPane.List.Items.Any(e => e.Name == "a.txt"), "o outro painel mostra a cópia");
        Assert.True(File.Exists(Path.Join(source, "a.txt")));
        if (app.Browser.List.SelectionCount > 0) d.Press(InputAction.Back); // limpa a marcação

        // Mover o item focado.
        await d.FocusItem("b.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Mover para o outro painel");
        var move = await d.WaitDialog("Mover 1 item?");
        Assert.Contains(("Para", target), move.Lines);
        d.ChooseOption(move, "Mover");
        await d.WaitDialog("Mover: concluído");
        d.Press(InputAction.Back);
        Assert.True(File.Exists(Path.Join(target, "b.txt")) && !File.Exists(Path.Join(source, "b.txt")));
        await d.Idle();

        // Extrair o compactado para o outro painel (numa pasta com o nome dele).
        await d.FocusItem("pack.zip");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Extrair para o outro painel");
        var extract = await d.WaitDialog("Extrair");
        Assert.Contains(("Origem", Path.Join(source, "pack.zip")), extract.Lines);
        Assert.Contains(extract.Lines, l => l.Label == "Destino" && l.Value.StartsWith(Path.Join(target, "pack"), StringComparison.Ordinal));
        d.ChooseOption(extract, "Extrair");
        await UiContext.WaitUntil(() => File.Exists(Path.Join(target, "pack", "leia-me.txt")), "extraído");
        await d.Idle();

        // Tela estreita (portátil): um painel só, com o foco no esquerdo; a escolha continua salva.
        app.SetDualPaneFits(false);
        Assert.False(app.DualPaneActive);
        Assert.True(app.Settings.DualPane);
        Assert.Same(app.LeftPane, app.Browser);
        Assert.DoesNotContain(app.Hints, h => h.Action == InputAction.SwitchPane);
    });

    [Fact]
    public void Other_pane_actions_are_unavailable_with_a_reason_when_both_panes_show_the_same_folder() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Dois painéis: desligado");
        await d.FocusItem("a.txt");

        d.Press(InputAction.OpenContextMenu);
        var menu = await d.WaitMenu();
        var copy = menu.Items.Single(i => i.Label == "Copiar para o outro painel");
        Assert.False(copy.IsEnabled);
        Assert.Equal("Os dois painéis estão na mesma pasta.", copy.DisabledReason);
    });
}
