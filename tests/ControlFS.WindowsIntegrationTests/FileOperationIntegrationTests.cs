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
}
