using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Operations;

public class FileOperationServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly FileOperationService _service = new();

    public void Dispose() => _tmp.Dispose();

    internal sealed class Scripted(params ConflictDecision[] decisions) : IConflictInteraction
    {
        private readonly Queue<ConflictDecision> _decisions = new(decisions);
        public List<ConflictInfo> Seen { get; } = [];

        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken)
        {
            Seen.Add(conflict);
            return Task.FromResult(_decisions.Count > 0 ? _decisions.Dequeue() : throw new InvalidOperationException("Conflito inesperado: " + conflict.ExistingPath));
        }
    }

    private Task<OperationResult> Run(FileOperationKind kind, string dest, IConflictInteraction? conflicts = null, CancellationToken ct = default, params string[] sources) =>
        _service.RunAsync(new FileOperationRequest { Kind = kind, Sources = sources, DestinationFolder = dest }, conflicts ?? new Scripted(), null, ct);

    private string MakeTree()
    {
        var root = _tmp.MakeDir("origem", "Fotos");
        File.WriteAllText(Path.Join(root, "ação.txt"), "a");
        Directory.CreateDirectory(Path.Join(root, "sub"));
        File.WriteAllBytes(Path.Join(root, "sub", "dados.bin"), Enumerable.Range(0, 300_000).Select(i => (byte)(i % 253)).ToArray());
        File.SetLastWriteTimeUtc(Path.Join(root, "ação.txt"), new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc));
        return root;
    }

    private static void AssertNoTemps(string dir) =>
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories), p => Path.GetFileName(p).StartsWith(".controlfs-", StringComparison.Ordinal));

    [Fact]
    public async Task Copies_files_and_folders_byte_identical_and_keeps_originals()
    {
        var tree = MakeTree();
        var dest = _tmp.MakeDir("destino");

        var result = await Run(FileOperationKind.Copy, dest, sources: tree);

        Assert.Equal(OperationState.Completed, result.FinalState);
        Assert.Equal(File.ReadAllBytes(Path.Join(tree, "sub", "dados.bin")), File.ReadAllBytes(Path.Join(dest, "Fotos", "sub", "dados.bin")));
        Assert.Equal(new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(Path.Join(dest, "Fotos", "ação.txt")));
        Assert.True(File.Exists(Path.Join(tree, "ação.txt")), "a cópia não altera a origem");
        AssertNoTemps(dest);
    }

    [Fact]
    public async Task Copying_or_moving_a_folder_into_itself_is_refused_before_touching_the_disk()
    {
        var tree = MakeTree();
        var before = _tmp.Snapshot();
        var copy = await Run(FileOperationKind.Copy, Path.Join(tree, "sub"), sources: tree);
        var move = await Run(FileOperationKind.Move, tree, sources: tree);
        Assert.Equal(OperationErrorKind.PathRejected, copy.Error);
        Assert.Equal(OperationErrorKind.PathRejected, move.Error);
        Assert.Equal(before, _tmp.Snapshot());
    }

    [Fact]
    public async Task File_conflicts_follow_the_user_decision_and_never_overwrite_silently()
    {
        var src = _tmp.MakeDir("src");
        var dest = _tmp.MakeDir("dest");
        foreach (var n in new[] { "a.txt", "b.txt", "c.txt" })
        {
            File.WriteAllText(Path.Join(src, n), "NOVO");
            File.WriteAllText(Path.Join(dest, n), "velho");
        }
        var conflicts = new Scripted(new ConflictDecision(ConflictChoice.Skip), new ConflictDecision(ConflictChoice.KeepBoth), new ConflictDecision(ConflictChoice.Replace));

        var result = await Run(FileOperationKind.Copy, dest, conflicts, default, Path.Join(src, "a.txt"), Path.Join(src, "b.txt"), Path.Join(src, "c.txt"));

        Assert.Equal([ItemOutcome.Skipped, ItemOutcome.Renamed, ItemOutcome.Replaced], result.Items.Select(i => i.Outcome));
        Assert.Equal("velho", File.ReadAllText(Path.Join(dest, "a.txt")));
        Assert.Equal("NOVO", File.ReadAllText(Path.Join(dest, "b (2).txt")));
        Assert.Equal("NOVO", File.ReadAllText(Path.Join(dest, "c.txt")));
        AssertNoTemps(dest);
    }

    [Fact]
    public async Task Folder_into_existing_folder_merges_only_when_chosen()
    {
        var tree = MakeTree();
        var dest = _tmp.MakeDir("dest");
        Directory.CreateDirectory(Path.Join(dest, "Fotos"));
        File.WriteAllText(Path.Join(dest, "Fotos", "ação.txt"), "existente");
        File.WriteAllText(Path.Join(dest, "Fotos", "só-no-destino.txt"), "fica");

        var skip = new Scripted(new ConflictDecision(ConflictChoice.Skip));
        await Run(FileOperationKind.Copy, dest, skip, default, tree);
        Assert.True(skip.Seen.Single().IsFolderMerge);
        Assert.False(Directory.Exists(Path.Join(dest, "Fotos", "sub")));

        var merge = new Scripted(new ConflictDecision(ConflictChoice.Replace), new ConflictDecision(ConflictChoice.KeepBoth));
        var result = await Run(FileOperationKind.Copy, dest, merge, default, tree);

        Assert.Equal(2, merge.Seen.Count); // pasta (mesclar) e depois o arquivo com o mesmo nome
        Assert.Equal("existente", File.ReadAllText(Path.Join(dest, "Fotos", "ação.txt")));
        Assert.Equal("a", File.ReadAllText(Path.Join(dest, "Fotos", "ação (2).txt")));
        Assert.True(File.Exists(Path.Join(dest, "Fotos", "só-no-destino.txt")));
        Assert.True(File.Exists(Path.Join(dest, "Fotos", "sub", "dados.bin")));
        Assert.Equal(OperationState.Completed, result.FinalState);
    }

    [Fact]
    public async Task Move_on_the_same_volume_relocates_and_same_folder_is_a_no_op()
    {
        var tree = MakeTree();
        var dest = _tmp.MakeDir("dest");
        var same = await Run(FileOperationKind.Move, Path.GetDirectoryName(tree)!, sources: tree);
        Assert.Equal(ItemOutcome.Skipped, Assert.Single(same.Items).Outcome);

        var result = await Run(FileOperationKind.Move, dest, sources: tree);

        Assert.Equal(OperationState.Completed, result.FinalState);
        Assert.False(Directory.Exists(tree));
        Assert.True(File.Exists(Path.Join(dest, "Fotos", "sub", "dados.bin")));
    }

    [Fact]
    public async Task Links_inside_folders_are_not_followed()
    {
        var tree = MakeTree();
        var outside = _tmp.MakeDir("fora");
        File.WriteAllText(Path.Join(outside, "segredo.txt"), "x");
        try { Directory.CreateSymbolicLink(Path.Join(tree, "atalho"), outside); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Assert.Skip("Sem privilégio para criar symlink."); }
        var dest = _tmp.MakeDir("dest");

        var result = await Run(FileOperationKind.Copy, dest, sources: tree);

        Assert.Contains(result.Items, i => i.Error == OperationErrorKind.LinkOrSpecialBlocked);
        Assert.False(Directory.Exists(Path.Join(dest, "Fotos", "atalho")));
    }

    [Fact]
    public async Task Cancelled_operation_leaves_no_partial_files()
    {
        var tree = MakeTree();
        var dest = _tmp.MakeDir("dest");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await Run(FileOperationKind.Copy, dest, null, cts.Token, tree);
        Assert.Equal(OperationState.Cancelled, result.FinalState);
        Assert.Empty(Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Cancelling_at_a_conflict_reports_the_remaining_items_as_not_processed_with_their_sources()
    {
        var src = _tmp.MakeDir("origem");
        var dest = _tmp.MakeDir("destino");
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" }) File.WriteAllText(Path.Join(src, name), name);
        File.WriteAllText(Path.Join(dest, "a.txt"), "existente");

        var result = await Run(FileOperationKind.Copy, dest, new Scripted(new ConflictDecision(ConflictChoice.Cancel)), default,
            Path.Join(src, "a.txt"), Path.Join(src, "b.txt"), Path.Join(src, "c.txt"));

        Assert.Equal(OperationState.Cancelled, result.FinalState);
        Assert.True(result.Items[0].NeedsRetry, "o item interrompido no conflito também deve ser refeito");
        Assert.All(result.Items.Skip(1), i => Assert.Equal(ItemOutcome.NotProcessed, i.Outcome));
        Assert.Equal([Path.Join(src, "a.txt"), Path.Join(src, "b.txt"), Path.Join(src, "c.txt")], result.Items.Select(i => i.SourcePath));
        Assert.All(result.Items, i => Assert.Equal(dest, i.TargetFolder));
        Assert.False(File.Exists(Path.Join(dest, "b.txt")));
    }

    /// <summary>Progresso síncrono: chamado na própria thread do motor, logo depois de cada bloco gravado.</summary>
    private sealed class SyncProgress(Action<OperationProgress> onReport) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => onReport(value);
    }

    private (string Source, string Dest, PauseGate Gate, SyncProgress Progress, Func<long> Bytes) PauseOnFirstBlock()
    {
        var source = _tmp.Sub("grande.bin");
        File.WriteAllBytes(source, Enumerable.Range(0, 4_000_000).Select(i => (byte)(i % 251)).ToArray());
        var dest = _tmp.MakeDir("destino");
        var gate = new PauseGate();
        long bytes = 0;
        var progress = new SyncProgress(p =>
        {
            Interlocked.Exchange(ref bytes, p.BytesProcessed);
            if (p.BytesProcessed > 0) gate.Pause();
        });
        return (source, dest, gate, progress, () => Interlocked.Read(ref bytes));
    }

    [Fact]
    public async Task Pausing_mid_file_stops_writing_and_resuming_finishes_the_copy_intact()
    {
        var (source, dest, gate, progress, bytes) = PauseOnFirstBlock();
        var run = _service.RunAsync(new FileOperationRequest { Kind = FileOperationKind.Copy, Sources = [source], DestinationFolder = dest, Pause = gate },
            new Scripted(), progress, default);

        await UiContext.WaitUntil(() => gate.IsPaused, "pausa no primeiro bloco");
        var atPause = bytes();
        await Task.Delay(1000);
        Assert.Equal(atPause, bytes()); // nenhum bloco a mais enquanto pausado
        Assert.True(atPause < new FileInfo(source).Length);
        Assert.False(File.Exists(Path.Join(dest, "grande.bin")), "nada pela metade no destino: só o temporário oculto");
        Assert.False(run.IsCompleted);

        gate.Resume();
        var result = await run;
        Assert.Equal(OperationState.Completed, result.FinalState);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Join(dest, "grande.bin")));
        AssertNoTemps(dest);
    }

    [Fact]
    public async Task Cancelling_while_paused_removes_the_partial_copy()
    {
        var (source, dest, gate, progress, _) = PauseOnFirstBlock();
        using var cts = new CancellationTokenSource();
        var run = _service.RunAsync(new FileOperationRequest { Kind = FileOperationKind.Copy, Sources = [source], DestinationFolder = dest, Pause = gate },
            new Scripted(), progress, cts.Token);
        await UiContext.WaitUntil(() => gate.IsPaused, "pausa no primeiro bloco");

        cts.Cancel(); // sem continuar antes
        var result = await run;

        Assert.Equal(OperationState.Cancelled, result.FinalState);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dest));
        Assert.Equal(4_000_000, new FileInfo(source).Length);
    }
}
