using System.Diagnostics;
using ControlFS.Application;
using ControlFS.Application.Operations;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Sem travar e com andamento consistente (#257): fila de UI, porcentagem, estimativa e cancelamento.</summary>
public class ProgressJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    /// <summary>Motor de teste: o teste decide o que relatar e quando terminar.</summary>
    private sealed class Scripted(Func<FileOperationRequest, IProgress<OperationProgress>?, CancellationToken, Task<OperationResult>> run) : IFileOperationService
    {
        public Task<OperationResult> RunAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
            run(request, progress, cancellationToken);

        public bool CanRecycle(string path) => false;
        public FileEntry Rename(string path, string newName) => throw new NotSupportedException();
    }

    private async Task<(Driver Driver, OperationItem Op)> StartCopy(IFileOperationService service, FileOperationKind kind = FileOperationKind.Copy)
    {
        File.WriteAllText(_tmp.Sub("nota.txt"), "conteúdo");
        _tmp.MakeDir("Destino");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: service);
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        app.ConfirmTransfer(kind, [_tmp.Sub("nota.txt")], _tmp.Sub("Destino"), _tmp.Path);
        d.ChooseOption(await d.WaitDialog(kind == FileOperationKind.Copy ? "Copiar 1 item?" : "Mover 1 item?"), kind == FileOperationKind.Copy ? "Copiar" : "Mover");
        return (d, app.Operations.Items[^1]);
    }

    [Fact]
    public void Coalescing_delivers_the_latest_value_at_a_bounded_rate() => UiContext.Run(async () =>
    {
        var delivered = new List<int>();
        var progress = new CoalescingProgress<int>(delivered.Add, TimeSpan.FromMilliseconds(100));
        var last = 0;
        await Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 600) progress.Report(++last);
        });
        await UiContext.WaitUntil(() => delivered.Count > 0 && delivered[^1] == last, "último valor entregue");
        Assert.True(last > 1000, "o motor relatou muito mais do que a UI recebeu");
        Assert.InRange(delivered.Count, 4, 12); // ~10 por segundo durante 0,6 s + o último
        Assert.Equal(delivered.Order().ToList(), delivered); // nunca fora de ordem
    });

    [Fact]
    public void Flood_of_progress_reports_neither_freezes_nor_floods_the_ui_thread() => UiContext.Run(async () =>
    {
        // Antes da correção cada bloco de 80 KB virava um Post e um redesenho: a fila da UI enchia e o app travava após a cópia.
        long reports = 0;
        var service = new Scripted(async (_, progress, _) =>
        {
            await Task.Run(() =>
            {
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 1500) progress!.Report(new OperationProgress("grande.bin", 0, 1, Interlocked.Increment(ref reports) * 81_920L, 50_000_000_000));
            }, CancellationToken.None);
            return new OperationResult(OperationState.Completed, []);
        });

        var ui = SynchronizationContext.Current!;
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: service);
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();

        var redraws = 0;
        var running = false;
        app.Changed += () =>
        {
            if (!running) return;
            redraws++;
            var spin = Stopwatch.StartNew();
            while (spin.Elapsed.TotalMilliseconds < 3) { } // um redesenho custa alguns ms
        };
        File.WriteAllText(_tmp.Sub("nota.txt"), "x");
        _tmp.MakeDir("Destino");
        running = true;
        app.ConfirmTransfer(FileOperationKind.Copy, [_tmp.Sub("nota.txt")], _tmp.Sub("Destino"), _tmp.Path);
        d.ChooseOption(await d.WaitDialog("Copiar 1 item?"), "Copiar");
        var op = app.Operations.Items[^1];

        // Sonda: posta um retorno na "UI" a cada 20 ms e mede quanto ele demora para rodar.
        long worst = 0;
        var probing = true;
        var probe = Task.Run(async () =>
        {
            while (Volatile.Read(ref probing))
            {
                var posted = Stopwatch.GetTimestamp();
                ui.Post(_ => Interlocked.Exchange(ref worst, Math.Max(Interlocked.Read(ref worst), (long)Stopwatch.GetElapsedTime(posted).TotalMilliseconds)), null);
                await Task.Delay(20);
            }
        });
        await UiContext.WaitUntil(() => !op.IsActive, "operação concluída", timeoutMs: 30_000);
        running = false;
        Volatile.Write(ref probing, false);
        await probe;

        Assert.True(Interlocked.Read(ref reports) > 3000, "o teste precisa inundar de relatos");
        Assert.True(redraws <= 40, $"{redraws} redesenhos em ~1,5 s: o andamento não foi limitado");
        Assert.True(Interlocked.Read(ref worst) < 500, $"a UI ficou {worst} ms sem responder");
        Assert.Equal(OperationState.Completed, op.State);
    });

    [Fact]
    public void Copy_reports_kind_items_bytes_and_a_fraction_that_never_goes_backwards() => UiContext.Run(async () =>
    {
        var big = _tmp.Sub("grande.bin");
        await File.WriteAllBytesAsync(big, new byte[48 << 20]);
        _tmp.MakeDir("Pasta");
        for (var i = 0; i < 150; i++) File.WriteAllText(_tmp.Sub("Pasta", $"f{i}.txt"), new string('a', 2000));
        _tmp.MakeDir("Destino");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();

        var seen = new List<OperationProgress>();
        var fractions = new List<double>();
        app.Changed += () =>
        {
            if (app.Operations.Current is { Progress: { } p } op)
            {
                seen.Add(p);
                if (op.Fraction is { } f) fractions.Add(f);
            }
        };
        app.ConfirmTransfer(FileOperationKind.Copy, [big, _tmp.Sub("Pasta")], _tmp.Sub("Destino"), _tmp.Path);
        d.ChooseOption(await d.WaitDialog("Copiar 2 itens?"), "Copiar");
        var item = app.Operations.Items[^1];
        await UiContext.WaitUntil(() => !item.IsActive, "cópia concluída", timeoutMs: 40_000);

        Assert.Equal(OperationState.Completed, item.State);
        Assert.NotEmpty(seen);
        Assert.All(seen.Where(p => !p.Indeterminate), p =>
        {
            Assert.Equal(OperationKind.Copy, p.Kind);
            Assert.Equal(_tmp.Sub("Destino"), p.Destination);
            Assert.Equal(_tmp.Path, p.Source);
            Assert.Equal(151, p.ItemsTotal);
            Assert.Equal((48L << 20) + 150 * 2000, p.BytesTotal);
        });
        Assert.Contains(seen, p => p is { Indeterminate: false, BytesProcessed: > 0 });
        Assert.Equal(fractions.Order().ToList(), fractions);
        Assert.Equal(150, Directory.GetFiles(_tmp.Sub("Destino", "Pasta")).Length);
        Assert.Equal(48 << 20, new FileInfo(_tmp.Sub("Destino", "grande.bin")).Length);
    });

    [Fact]
    public void Batch_delete_reports_by_items_because_bytes_are_not_measured() => UiContext.Run(async () =>
    {
        for (var i = 0; i < 40; i++) File.WriteAllText(_tmp.Sub($"a{i}.txt"), "x");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        var seen = new List<OperationProgress>();
        app.Changed += () =>
        {
            if (app.Operations.Current?.Progress is { } p) seen.Add(p);
        };
        app.EnqueueFileOperation(new AppController.FileOperationPlan(FileOperationKind.Delete, Enumerable.Range(0, 40).Select(i => _tmp.Sub($"a{i}.txt")).ToList(), null, Permanent: true, _tmp.Path));
        var item = app.Operations.Items[^1];
        await UiContext.WaitUntil(() => !item.IsActive, "exclusão concluída");

        Assert.NotEmpty(seen);
        Assert.All(seen, p =>
        {
            Assert.Equal(OperationKind.Delete, p.Kind);
            Assert.Equal(40, p.ItemsTotal);
            Assert.Null(p.BytesTotal);
        });
        Assert.Empty(Directory.GetFiles(_tmp.Path, "a*.txt"));
    });

    [Fact]
    public void Cancelling_mid_operation_ends_cancelled_and_late_reports_do_not_revive_the_progress() => UiContext.Run(async () =>
    {
        var reported = new TaskCompletionSource();
        IProgress<OperationProgress>? saved = null;
        var service = new Scripted(async (_, progress, ct) =>
        {
            saved = progress;
            progress!.Report(new OperationProgress("a.bin", 1, 4, 25, 100));
            reported.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new OperationResult(OperationState.Completed, []);
        });
        var (d, op) = await StartCopy(service);
        await reported.Task;
        await UiContext.WaitUntil(() => op.Progress is not null, "primeiro andamento");
        Assert.Equal(0.25, op.Fraction);

        Assert.True(d.App.Operations.Cancel(op));
        await UiContext.WaitUntil(() => !op.IsActive, "cancelada");
        Assert.Equal(OperationState.Cancelled, op.State);

        var before = op.Progress;
        saved!.Report(new OperationProgress("tarde", 4, 4, 100, 100));
        await Task.Delay(300);
        Assert.Same(before, op.Progress);
        Assert.Null(d.App.Operations.Progress);
    });

    [Fact]
    public void Cut_only_marks_the_clipboard_and_the_move_shows_progress_only_when_pasted() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        _tmp.MakeDir("Destino");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new FileOperationService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Recortar");
        Assert.Empty(app.Operations.Items);
        Assert.Null(app.Operations.Progress);

        await d.FocusItem("Destino");
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Colar 1 item (mover)");
        d.ChooseOption(await d.WaitDialog("Mover 1 item?"), "Mover");
        var op = app.Operations.Items[^1];
        Assert.Equal(OperationKind.Move, op.Kind);
        await UiContext.WaitUntil(() => !op.IsActive, "movimentação concluída");
        Assert.Equal(OperationState.Completed, op.State);
    });

    [Fact]
    public void Unmeasurable_work_shows_the_activity_without_percent_or_estimate() => UiContext.Run(async () =>
    {
        var reported = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var service = new Scripted(async (_, progress, _) =>
        {
            progress!.Report(new OperationProgress("listando…", 7, null, 0, null) { Indeterminate = true });
            reported.SetResult();
            await release.Task;
            return new OperationResult(OperationState.Completed, []);
        });
        var (d, op) = await StartCopy(service);
        await reported.Task;
        await UiContext.WaitUntil(() => op.Progress is not null, "andamento");
        Assert.Null(op.Fraction);

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Operações");
        var row = Assert.Single((await d.WaitMenu()).Items, i => i.Section == "Em andamento");
        Assert.DoesNotContain("%", row.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("estimativa", row.Detail, StringComparison.Ordinal);
        await d.ChooseMenu("Copiar 1 item");
        var details = await d.WaitDialog("Copiar 1 item");
        Assert.Null(details.Progress);
        Assert.Contains(details.Lines, l => l.Label == "Atual" && l.Value == "listando…");
        Assert.Contains(details.Lines, l => l.Label == "Andamento" && l.Value.StartsWith("sem total", StringComparison.Ordinal));
        d.ChooseOption(details, "Fechar");
        release.SetResult();
        await UiContext.WaitUntil(() => !op.IsActive, "concluída");
    });

    [Fact]
    public void Details_of_a_running_copy_show_percent_items_data_and_follow_the_progress() => UiContext.Run(async () =>
    {
        var step = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var service = new Scripted(async (_, progress, _) =>
        {
            progress!.Report(new OperationProgress("a.bin", 1, 4, 25 << 20, 100 << 20));
            await step.Task;
            progress.Report(new OperationProgress("b.bin", 2, 4, 50 << 20, 100 << 20));
            await release.Task;
            return new OperationResult(OperationState.Completed, []);
        });
        var (d, op) = await StartCopy(service);
        await UiContext.WaitUntil(() => op.Progress is not null, "andamento");

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Operações");
        await d.ChooseMenu("Copiar 1 item");
        var details = await d.WaitDialog("Copiar 1 item");
        Assert.Equal(0.25, details.Progress);
        Assert.Contains(details.Lines, l => l.Label == "Itens" && l.Value == "1 de 4 itens");
        Assert.Contains(details.Lines, l => l.Label == "Dados" && l.Value == "25 MB de 100 MB");
        Assert.Contains(details.Lines, l => l.Label == "Origem" && l.Value == _tmp.Path);
        Assert.Contains(details.Lines, l => l.Label == "Destino" && l.Value == _tmp.Sub("Destino"));

        step.SetResult(); // o relato seguinte chega depois do intervalo e atualiza o diálogo aberto
        await UiContext.WaitUntil(() => details.Progress == 0.5, "diálogo acompanha o andamento");
        Assert.Contains(details.Lines, l => l.Label == "Atual" && l.Value == "b.bin");
        d.ChooseOption(details, "Fechar");
        release.SetResult();
        await UiContext.WaitUntil(() => !op.IsActive, "concluída");
    });

    [Fact]
    public async Task Compression_and_extraction_report_items_and_bytes_with_known_totals()
    {
        var source = _tmp.MakeDir("Fonte");
        for (var i = 0; i < 5; i++) File.WriteAllBytes(Path.Join(source, $"f{i}.bin"), new byte[300_000]);
        var archive = _tmp.Sub("saida.zip");
        var service = new ArchiveService();
        var compress = new List<OperationProgress>();
        var compressed = await service.CompressAsync(new CompressionRequest { SourcePaths = [source], DestinationPath = archive }, new Immediate(compress), default);
        Assert.Equal(OperationState.Completed, compressed.FinalState);
        Assert.All(compress, p => Assert.Equal(1_500_000, p.BytesTotal));
        Assert.Equal(1_500_000, compress[^1].BytesProcessed);

        var extract = new List<OperationProgress>();
        var extracted = await service.ExtractAsync(new ExtractionRequest { ArchivePath = archive, DestinationDirectory = _tmp.MakeDir("Fora"), Mode = DestinationMode.IntoExistingFolder, },
            new NoConflicts(), new Immediate(extract), default);
        Assert.Equal(OperationState.Completed, extracted.FinalState);
        Assert.All(extract, p => Assert.Equal(1_500_000, p.BytesTotal));
        Assert.All(extract, p => Assert.Equal(5, p.ItemsTotal));
        Assert.Equal(1_500_000, extract[^1].BytesProcessed);
        Assert.Equal(extract.Select(p => p.BytesProcessed).Order().ToList(), extract.Select(p => p.BytesProcessed).ToList());
    }

    private sealed class Immediate(List<OperationProgress> into) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => into.Add(value);
    }

    private sealed class NoConflicts : IExtractionInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            Task.FromResult(new ConflictDecision(ConflictChoice.Skip));
    }
}
