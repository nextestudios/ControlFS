using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Archives;

public class ArchiveCreatorTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly ArchiveService _service = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class NoConflicts : IExtractionInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("conflito inesperado");
    }

    private string MakeSource()
    {
        var root = _tmp.MakeDir("origem", "Relatórios");
        File.WriteAllText(Path.Join(root, "ação.txt"), "primeiro");
        Directory.CreateDirectory(Path.Join(root, "sub"));
        File.WriteAllBytes(Path.Join(root, "sub", "dados.bin"), Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray());
        File.WriteAllText(_tmp.Sub("origem", "solto.txt"), "solto");
        return root;
    }

    [Theory]
    [InlineData(CompressionFormat.Zip, ArchiveFormat.Zip)]
    [InlineData(CompressionFormat.TarGZip, ArchiveFormat.TarGZip)]
    public async Task Round_trip_create_then_extract(CompressionFormat format, ArchiveFormat detected)
    {
        var folder = MakeSource();
        var destination = _tmp.Sub("origem", "pacote" + CompressionRequest.Extension(format));

        var created = await _service.CompressAsync(new CompressionRequest
        {
            SourcePaths = [folder, _tmp.Sub("origem", "solto.txt")],
            DestinationPath = destination,
            Format = format,
        }, null, CancellationToken.None);

        Assert.Equal(OperationState.Completed, created.FinalState);
        Assert.Equal(3, created.Count(ItemOutcome.Succeeded));
        Assert.Equal(detected, _service.Detect(destination));
        Assert.DoesNotContain(Directory.EnumerateFiles(_tmp.Sub("origem")), f => Path.GetFileName(f).StartsWith(".controlfs-", StringComparison.Ordinal));

        var extracted = await _service.ExtractAsync(new ExtractionRequest { ArchivePath = destination, DestinationDirectory = _tmp.MakeDir("volta") }, new NoConflicts(), null, CancellationToken.None);
        Assert.Equal(OperationState.Completed, extracted.FinalState);
        var root = extracted.Destination!;
        Assert.Equal("primeiro", File.ReadAllText(Path.Join(root, "Relatórios", "ação.txt")));
        Assert.Equal(File.ReadAllBytes(Path.Join(folder, "sub", "dados.bin")), File.ReadAllBytes(Path.Join(root, "Relatórios", "sub", "dados.bin")));
        Assert.Equal("solto", File.ReadAllText(Path.Join(root, "solto.txt")));
    }

    [Fact]
    public async Task Existing_file_is_never_overwritten()
    {
        var folder = MakeSource();
        var destination = _tmp.Sub("origem", "existe.zip");
        File.WriteAllText(destination, "original");
        var result = await _service.CompressAsync(new CompressionRequest { SourcePaths = [folder], DestinationPath = destination }, null, CancellationToken.None);
        Assert.Equal(OperationErrorKind.AlreadyExists, result.Error);
        Assert.Equal("original", File.ReadAllText(destination));
    }

    [Fact]
    public async Task Archive_inside_a_source_folder_is_refused()
    {
        var folder = MakeSource();
        var result = await _service.CompressAsync(new CompressionRequest { SourcePaths = [folder], DestinationPath = Path.Join(folder, "eu.zip") }, null, CancellationToken.None);
        Assert.Equal(OperationErrorKind.PathRejected, result.Error);
        Assert.False(File.Exists(Path.Join(folder, "eu.zip")));
    }

    [Fact]
    public async Task Links_are_not_followed()
    {
        var folder = MakeSource();
        var outside = _tmp.MakeDir("fora");
        File.WriteAllText(Path.Join(outside, "segredo.txt"), "não deveria entrar");
        try { Directory.CreateSymbolicLink(Path.Join(folder, "atalho"), outside); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Assert.Skip("Sem privilégio para criar symlink neste ambiente."); }
        var destination = _tmp.Sub("links.zip");

        var result = await _service.CompressAsync(new CompressionRequest { SourcePaths = [folder], DestinationPath = destination }, null, CancellationToken.None);

        Assert.Equal(OperationState.CompletedWithWarnings, result.FinalState);
        Assert.Contains(result.Items, i => i.Error == OperationErrorKind.LinkOrSpecialBlocked);
        var info = await _service.InspectAsync(destination, null, ControlFS.Core.Policies.ExtractionLimits.Default, CancellationToken.None);
        Assert.DoesNotContain(info.Entries, e => e.RawKey.Contains("segredo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_leaves_nothing_behind()
    {
        var folder = MakeSource();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var destination = _tmp.Sub("cancelado.zip");
        var result = await _service.CompressAsync(new CompressionRequest { SourcePaths = [folder], DestinationPath = destination }, null, cts.Token);
        Assert.Equal(OperationState.Cancelled, result.FinalState);
        Assert.False(File.Exists(destination));
        Assert.DoesNotContain(Directory.EnumerateFiles(_tmp.Path), f => Path.GetFileName(f).StartsWith(".controlfs-", StringComparison.Ordinal));
    }
}
