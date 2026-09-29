using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

public sealed class FileOperationIntegrationTests : IDisposable
{
    private readonly List<string> _roots = [];
    private readonly FileOperationService _service = new();

    public FileOperationIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
    }

    public void Dispose()
    {
        foreach (var r in _roots) try { Directory.Delete(r, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Root(string parent)
    {
        var r = Path.Join(parent, "controlfs-ops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(r);
        _roots.Add(r);
        return r;
    }

    private sealed class NoConflicts : IConflictInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Conflito inesperado");
    }

    [Fact]
    public async Task Move_across_volumes_copies_then_removes_each_source()
    {
        // No runner do GitHub o workspace fica em D: e o TEMP em C: — dois volumes reais.
        var here = Path.GetPathRoot(AppContext.BaseDirectory)!;
        var other = Path.GetPathRoot(Path.GetTempPath())!;
        if (string.Equals(here, other, StringComparison.OrdinalIgnoreCase)) Assert.Skip("Precisa de dois volumes.");
        var src = Root(Path.Join(here, "controlfs-it"));
        var dest = Root(Path.GetTempPath());
        Directory.CreateDirectory(Path.Join(src, "pasta", "sub"));
        File.WriteAllText(Path.Join(src, "pasta", "sub", "a.txt"), "conteúdo");

        var result = await _service.RunAsync(new FileOperationRequest { Kind = FileOperationKind.Move, Sources = [Path.Join(src, "pasta")], DestinationFolder = dest },
            new NoConflicts(), null, CancellationToken.None);

        Assert.Equal(OperationState.Completed, result.FinalState);
        Assert.Equal("conteúdo", File.ReadAllText(Path.Join(dest, "pasta", "sub", "a.txt")));
        Assert.False(Directory.Exists(Path.Join(src, "pasta")));
    }

    [Fact]
    public async Task A_locked_file_fails_alone_and_the_rest_of_the_batch_continues()
    {
        var src = Root(Path.GetTempPath());
        var dest = Root(Path.GetTempPath());
        File.WriteAllText(Path.Join(src, "livre.txt"), "ok");
        File.WriteAllText(Path.Join(src, "preso.txt"), "x");
        await using var hold = new FileStream(Path.Join(src, "preso.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await _service.RunAsync(new FileOperationRequest { Kind = FileOperationKind.Copy, Sources = [Path.Join(src, "preso.txt"), Path.Join(src, "livre.txt")], DestinationFolder = dest },
            new NoConflicts(), null, CancellationToken.None);

        Assert.Equal(OperationState.CompletedWithWarnings, result.FinalState);
        Assert.Equal(ItemOutcome.Failed, result.Items.Single(i => i.Name == "preso.txt").Outcome);
        Assert.True(File.Exists(Path.Join(dest, "livre.txt")));
        Assert.False(File.Exists(Path.Join(dest, "preso.txt")));
    }

    private sealed class Recorder : IProgress<OperationProgress>
    {
        public List<OperationProgress> Seen { get; } = [];
        public void Report(OperationProgress value) => Seen.Add(value);
    }

    [Fact]
    public async Task Cross_volume_move_of_a_large_file_and_many_small_files_reports_growing_bytes_up_to_the_total()
    {
        var here = Path.GetPathRoot(AppContext.BaseDirectory)!;
        var other = Path.GetPathRoot(Path.GetTempPath())!;
        if (string.Equals(here, other, StringComparison.OrdinalIgnoreCase)) Assert.Skip("Precisa de dois volumes.");
        var src = Root(Path.Join(here, "controlfs-it"));
        var dest = Root(Path.GetTempPath());
        var big = Path.Join(src, "grande.bin");
        await File.WriteAllBytesAsync(big, new byte[96 << 20]);
        Directory.CreateDirectory(Path.Join(src, "muitos"));
        for (var i = 0; i < 300; i++) File.WriteAllText(Path.Join(src, "muitos", $"f{i}.txt"), "x");
        var progress = new Recorder();

        var result = await _service.RunAsync(new FileOperationRequest { Kind = FileOperationKind.Move, Sources = [big, Path.Join(src, "muitos")], DestinationFolder = dest },
            new NoConflicts(), progress, CancellationToken.None);

        Assert.Equal(OperationState.Completed, result.FinalState);
        var measured = progress.Seen.Where(p => !p.Indeterminate).ToList();
        Assert.NotEmpty(measured);
        Assert.All(measured, p => Assert.Equal(301, p.ItemsTotal));
        Assert.Equal(measured.Select(p => p.BytesProcessed).Order().ToList(), measured.Select(p => p.BytesProcessed).ToList());
        Assert.Equal((96L << 20) + 300, measured[^1].BytesProcessed);
        Assert.Equal(301, measured[^1].ItemsProcessed);
        // Blocos de 80 KB de um arquivo de 96 MB seriam mais de mil relatos: o motor limita o ruído.
        Assert.True(progress.Seen.Count < 800, $"{progress.Seen.Count} relatos");
    }
}
