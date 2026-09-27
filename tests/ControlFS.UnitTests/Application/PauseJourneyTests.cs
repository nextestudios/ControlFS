using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Pausar e continuar pelo Menu → Operações (#21).</summary>
public class PauseJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    /// <summary>Serviço real que só começa quando o teste manda (a operação já está "em andamento" na fila).</summary>
    private sealed class HeldService : IFileOperationService
    {
        private readonly FileOperationService _real = new();
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanRecycle(string path) => _real.CanRecycle(path);
        public FileEntry Rename(string path, string newName) => _real.Rename(path, newName);

        public async Task<OperationResult> RunAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
        {
            await Release.Task;
            return await _real.RunAsync(request, conflicts, progress, cancellationToken);
        }
    }

    [Fact]
    public void Copy_is_paused_and_resumed_from_the_operations_menu_while_extraction_offers_no_pause() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("nota.txt"), "conteúdo");
        _tmp.MakeDir("Destino");
        var service = new HeldService();
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: service);
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        app.ConfirmTransfer(FileOperationKind.Copy, [_tmp.Sub("nota.txt")], _tmp.Sub("Destino"), _tmp.Path);
        d.ChooseOption(await d.WaitDialog("Copiar 1 item(ns)?"), "Copiar");
        var op = app.Operations.Items[^1];
        Assert.Equal(OperationState.Running, op.State);

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Operações");
        await d.ChooseMenu("Copiar 1 item(ns) — em andamento");
        d.ChooseOption(await d.WaitDialog("Copiar 1 item(ns)"), "Pausar");
        Assert.Equal(OperationState.Paused, op.State);

        service.Release.SetResult(); // o motor chega ao primeiro ponto seguro e para ali
        await Task.Delay(500);
        Assert.False(File.Exists(_tmp.Sub("Destino", "nota.txt")));
        Assert.Equal(OperationState.Paused, op.State);

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Operações");
        await d.ChooseMenu("Copiar 1 item(ns) — pausada");
        var details = await d.WaitDialog("Copiar 1 item(ns)");
        Assert.DoesNotContain(details.Options, o => o.Label == "Pausar");
        d.ChooseOption(details, "Continuar");
        await d.WaitDialog("Copiar: concluído");
        Assert.Equal("conteúdo", File.ReadAllText(_tmp.Sub("Destino", "nota.txt")));
        Assert.False(op.CanPause);
        d.ChooseOption((ControlFS.Application.State.DialogModal)app.TopModal!, "Fechar");

        // Operação sem motor pausável (ex.: extração): a pausa não é oferecida nem simulada.
        var extraction = app.Operations.Enqueue("Extrair teste", OperationKind.Extract, async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new OperationResult(OperationState.Completed, []);
        });
        Assert.Equal(OperationState.Running, extraction.State);
        Assert.False(extraction.CanPause);
        Assert.False(app.Operations.Pause(extraction));
        app.Operations.Cancel(extraction);
        await UiContext.WaitUntil(() => !extraction.IsActive, "extração cancelada");
    });
}
