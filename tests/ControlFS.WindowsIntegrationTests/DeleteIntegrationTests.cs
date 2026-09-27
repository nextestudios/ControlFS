using System.Diagnostics;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

public sealed class DeleteIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-delete-" + Guid.NewGuid().ToString("N"));
    private readonly FileOperationService _service = new();

    public DeleteIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class NoConflicts : IConflictInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    [Fact]
    public async Task Delete_sends_the_file_to_the_recycle_bin()
    {
        var file = Path.Join(_root, "lixo.txt");
        File.WriteAllText(file, "x");
        if (!_service.CanRecycle(file)) Assert.Skip("Este runner não tem Lixeira na unidade do TEMP.");

        var result = await _service.RunAsync(new FileOperationRequest { Kind = FileOperationKind.Delete, Sources = [file] }, new NoConflicts(), null, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Succeeded, item.Outcome);
        Assert.Equal("Movido para a Lixeira.", item.Message);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Permanent_delete_removes_a_junction_but_never_what_it_points_to()
    {
        var outside = Path.Join(_root, "fora");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Join(outside, "importante.txt"), "fica");
        var victim = Path.Join(_root, "apagar");
        Directory.CreateDirectory(victim);
        File.WriteAllText(Path.Join(victim, "somente-leitura.txt"), "x");
        File.SetAttributes(Path.Join(victim, "somente-leitura.txt"), FileAttributes.ReadOnly);
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", Path.Join(victim, "atalho"), outside }, UseShellExecute = false, CreateNoWindow = true })!;
        await mklink.WaitForExitAsync();

        var result = await _service.RunAsync(new FileOperationRequest { Kind = FileOperationKind.Delete, Sources = [victim], Permanent = true }, new NoConflicts(), null, CancellationToken.None);

        Assert.Equal(OperationState.Completed, result.FinalState);
        Assert.False(Directory.Exists(victim));
        Assert.Equal("fica", File.ReadAllText(Path.Join(outside, "importante.txt")));
    }
}
