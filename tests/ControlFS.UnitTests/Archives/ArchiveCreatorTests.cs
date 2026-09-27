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
    [InlineData(CompressionFormat.SevenZip, ArchiveFormat.SevenZip)]
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

        Assert.True(created.FinalState == OperationState.Completed, $"{created.FinalState} {created.Error} {created.Message}");
        Assert.Equal(3, created.Count(ItemOutcome.Succeeded));
        Assert.Equal(detected, _service.Detect(destination));
        Assert.DoesNotContain(Directory.EnumerateFiles(_tmp.Sub("origem")), f => Path.GetFileName(f).StartsWith(".controlfs-", StringComparison.Ordinal));

        var extracted = await _service.ExtractAsync(new ExtractionRequest { ArchivePath = destination, DestinationDirectory = _tmp.MakeDir("volta") }, new NoConflicts(), null, CancellationToken.None);
        Assert.True(extracted.FinalState == OperationState.Completed, $"{extracted.FinalState} {extracted.Error} {extracted.Message} :: {string.Join(" | ", extracted.Items.Where(i => i.Outcome != ItemOutcome.Succeeded).Select(i => $"{i.Name}={i.Outcome}/{i.Error}/{i.Message}"))}");
        var root = extracted.Destination!;
        Assert.Equal("primeiro", File.ReadAllText(Path.Join(root, "Relatórios", "ação.txt")));
        Assert.Equal(File.ReadAllBytes(Path.Join(folder, "sub", "dados.bin")), File.ReadAllBytes(Path.Join(root, "Relatórios", "sub", "dados.bin")));
        Assert.Equal("solto", File.ReadAllText(Path.Join(root, "solto.txt")));
    }

    [Theory]
    [InlineData(CompressionStrength.Fast)]
    [InlineData(CompressionStrength.Maximum)]
    public async Task SevenZip_keeps_empty_files_and_folders_and_compresses(CompressionStrength strength)
    {
        // 7z guarda pastas e arquivos vazios fora do fluxo LZMA (bits "empty stream"/"empty file" no cabeçalho).
        var root = _tmp.MakeDir("origem", "caixa");
        Directory.CreateDirectory(Path.Join(root, "vazia"));
        File.WriteAllBytes(Path.Join(root, "zero.txt"), []);
        File.WriteAllText(Path.Join(root, "texto ção.txt"), string.Concat(Enumerable.Repeat("linha repetida para comprimir\n", 5000)));
        var destination = _tmp.Sub("origem", "caixa.7z");

        var created = await _service.CompressAsync(new CompressionRequest { SourcePaths = [root], DestinationPath = destination, Format = CompressionFormat.SevenZip, Strength = strength },
            null, CancellationToken.None);

        Assert.True(created.FinalState == OperationState.Completed, $"{created.FinalState} {created.Error} {created.Message}");
        Assert.True(new FileInfo(destination).Length < 10_000, $"{new FileInfo(destination).Length} bytes");
        var info = await _service.InspectAsync(destination, null, ControlFS.Core.Policies.ExtractionLimits.Default, CancellationToken.None);
        Assert.Equal(["caixa", "caixa/texto ção.txt", "caixa/vazia", "caixa/zero.txt"], info.Entries.Select(e => e.RawKey.Replace('\\', '/')).Order(StringComparer.Ordinal));
        var extracted = await _service.ExtractAsync(new ExtractionRequest { ArchivePath = destination, DestinationDirectory = _tmp.MakeDir("volta") }, new NoConflicts(), null, CancellationToken.None);
        Assert.True(extracted.FinalState == OperationState.Completed, $"{extracted.FinalState} {extracted.Error} {extracted.Message}");
        var back = Path.Join(extracted.Destination!, "caixa");
        Assert.True(Directory.Exists(Path.Join(back, "vazia")));
        Assert.Equal(0, new FileInfo(Path.Join(back, "zero.txt")).Length);
        Assert.Equal(File.ReadAllText(Path.Join(root, "texto ção.txt")), File.ReadAllText(Path.Join(back, "texto ção.txt")));
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
