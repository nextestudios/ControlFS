using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class DeleteJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    /// <summary>Serviço real com a Lixeira indisponível (como numa pasta de rede).</summary>
    private sealed class NoRecycleBin : IFileOperationService
    {
        private readonly FileOperationService _real = new();
        public List<FileOperationRequest> Requests { get; } = [];
        public bool CanRecycle(string path) => false;
        public FileEntry Rename(string path, string newName) => _real.Rename(path, newName);
        public Task<OperationResult> RunAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return _real.RunAsync(request, conflicts, progress, cancellationToken);
        }
    }

    [Fact]
    public void Without_a_recycle_bin_delete_is_explicitly_permanent_and_focus_moves_to_the_next_item() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        File.WriteAllText(_tmp.Sub("b.txt"), "b");
        File.WriteAllText(_tmp.Sub("c.txt"), "c");
        var ops = new NoRecycleBin();
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: ops);
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("b.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Excluir…");

        var dialog = await d.WaitDialog("Excluir 1 item(ns) permanentemente?");
        Assert.Equal("Cancelar", dialog.Options[dialog.FocusIndex].Label);
        Assert.DoesNotContain(dialog.Options, o => o.Label.Contains("Lixeira", StringComparison.Ordinal));
        d.Press(InputAction.Confirm); // Cancelar
        Assert.Empty(ops.Requests);
        Assert.True(File.Exists(_tmp.Sub("b.txt")));

        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Excluir…");
        d.ChooseOption(await d.WaitDialog("Excluir 1 item(ns) permanentemente?"), "Excluir permanentemente");
        await UiContext.WaitUntil(() => app.Operations.Items.Count == 1 && !app.Operations.Items[0].IsActive, "exclusão concluída");
        await d.Idle();

        Assert.True(Assert.Single(ops.Requests).Permanent);
        Assert.False(File.Exists(_tmp.Sub("b.txt")));
        Assert.Equal("c.txt", app.Browser.List.Focused?.Name);
    });
}
