using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class FileOperationJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private Driver Boot()
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        return new Driver(app);
    }

    [Fact]
    public void Copy_to_a_folder_with_the_picker_and_keep_both_on_conflict() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("nota.txt"), "nova");
        _tmp.MakeDir("Backup");
        File.WriteAllText(_tmp.Sub("Backup", "nota.txt"), "antiga");
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("nota.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Copiar para…");
        await d.Idle();
        Assert.Equal(Screen.FolderPicker, d.App.Screen);
        await d.FocusItem("Backup");
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Escolher esta pasta");
        var summary = await d.WaitDialog("Copiar 1 item(ns)?");
        Assert.Contains(summary.Lines, l => l.Label == "Para" && l.Value.EndsWith("Backup", StringComparison.Ordinal));
        d.ChooseOption(summary, "Copiar");

        var conflict = await d.WaitDialog("Já existe");
        Assert.Equal(0, conflict.FocusIndex); // começa em Pular (preserva o existente)
        d.ChooseOption(conflict, "Manter ambos");
        await d.WaitDialog("Copiar: concluído");

        Assert.Equal("antiga", File.ReadAllText(_tmp.Sub("Backup", "nota.txt")));
        Assert.Equal("nova", File.ReadAllText(_tmp.Sub("Backup", "nota (2).txt")));
        Assert.True(File.Exists(_tmp.Sub("nota.txt")));
    });

    [Fact]
    public void Move_marked_items_to_a_folder() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        File.WriteAllText(_tmp.Sub("b.txt"), "b");
        _tmp.MakeDir("Destino");
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.txt");
        d.Press(InputAction.ToggleSelection);
        await d.FocusItem("b.txt");
        d.Press(InputAction.ToggleSelection);
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Mover 2 item(ns) para…");
        await d.Idle();
        await d.FocusItem("Destino");
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Escolher esta pasta");
        d.ChooseOption(await d.WaitDialog("Mover 2 item(ns)?"), "Mover");
        await UiContext.WaitUntil(() => d.App.Operations.Items.Count == 1 && !d.App.Operations.Items[0].IsActive, "operação concluída");
        await d.Idle();

        Assert.False(File.Exists(_tmp.Sub("a.txt")));
        Assert.Equal("b", File.ReadAllText(_tmp.Sub("Destino", "b.txt")));
        Assert.DoesNotContain(d.App.Browser.List.Items, i => i.Name is "a.txt" or "b.txt"); // lista atualizada
    });

    [Fact]
    public void Retry_a_copy_that_failed_on_a_locked_file_after_unlocking_it() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("travado.txt"), "conteúdo");
        _tmp.MakeDir("Destino");
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.Idle();
        var locked = new FileStream(_tmp.Sub("travado.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        d.App.ConfirmTransfer(FileOperationKind.Copy, [_tmp.Sub("travado.txt")], _tmp.Sub("Destino"), _tmp.Path);
        d.ChooseOption(await d.WaitDialog("Copiar 1 item(ns)?"), "Copiar");
        var failed = await d.WaitDialog("Copiar: concluído com avisos");
        Assert.False(File.Exists(_tmp.Sub("Destino", "travado.txt")));
        d.ChooseOption(failed, "Fechar");
        locked.Dispose();

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Operações");
        await d.ChooseMenu("Copiar 1 item(ns)");
        d.ChooseOption(await d.WaitDialog("Copiar 1 item(ns)"), "Tentar de novo");
        await d.WaitDialog("Copiar: concluído");
        Assert.Equal("conteúdo", File.ReadAllText(_tmp.Sub("Destino", "travado.txt")));

        // Concluída sem avisos: não há o que tentar de novo.
        var retried = d.App.Operations.Items[^1];
        Assert.Equal(OperationState.Completed, retried.State);
        Assert.False(retried.CanRetry);
        d.ChooseOption((DialogModal)d.App.TopModal!, "Fechar");
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Operações");
        await d.ChooseMenu("Copiar 1 item(ns) — concluída");
        Assert.DoesNotContain((await d.WaitDialog("Copiar 1 item(ns)")).Options, o => o.Label == "Tentar de novo");
    });
}
