using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Archives;

/// <summary>
/// ZIP64 (#63). As fixtures são geradas no teste com o BCL (nada grande vai para o repositório): uma entrada acima de
/// 4 GiB (campos de tamanho ZIP64) e um arquivo com mais de 65.535 entradas (registro final ZIP64). Os dados passam
/// pelo <c>SafeExtractor</c> normal: limites, CRC e contenção continuam valendo.
/// </summary>
public class Zip64Tests : IDisposable
{
    private const int PatternPeriod = 8192; // menor que a janela do Deflate (32 KiB): comprime ~80:1, abaixo do limite de expansão
    private const long LargeSize = (4L << 30) + (1 << 20) + 123; // acima de uint.MaxValue, tamanho "quebrado" de propósito
    private const int ManyEntries = 70_000;

    private readonly TempDir _tmp = new();
    private readonly ArchiveService _service = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class NoConflicts : IExtractionInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("conflito inesperado");
    }

    private static byte[] Pattern()
    {
        var block = new byte[PatternPeriod];
        new Random(63).NextBytes(block);
        return block;
    }

    private static string Describe(OperationResult r) =>
        $"{r.FinalState} {r.Error} {r.Message} :: {string.Join(" | ", r.Items.Where(i => i.Outcome != ItemOutcome.Succeeded).Take(10).Select(i => $"{i.Name}={i.Outcome}/{i.Error}/{i.Message}"))}";

    private static bool HasZip64EndRecord(string path)
    {
        // Localizador do fim de diretório ZIP64 (PK\x06\x07) fica logo antes do registro final clássico (22 bytes sem comentário).
        using var file = File.OpenRead(path);
        var tail = new byte[42];
        file.Seek(-tail.Length, SeekOrigin.End);
        file.ReadExactly(tail);
        return BinaryPrimitives.ReadUInt32LittleEndian(tail) == 0x07064b50;
    }

    [Fact]
    [Trait("Category", "Slow")] // fora da CI dos pull requests; roda na release (ver AGENTS.md)
    public async Task Entry_larger_than_4_GiB_extracts_with_correct_size_crc_and_content()
    {
        var pattern = Pattern();
        var zip = _tmp.Sub("grande.zip");
        using (var stream = File.Create(zip))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var chunk = new byte[PatternPeriod * 128]; // 1 MiB, múltiplo do período: o padrão continua sem emenda
            for (var i = 0; i < chunk.Length; i += PatternPeriod) pattern.CopyTo(chunk, i);
            using (var s = archive.CreateEntry("dados/grande.bin", CompressionLevel.Fastest).Open())
            {
                for (long left = LargeSize; left > 0; left -= chunk.Length)
                    s.Write(chunk, 0, (int)Math.Min(chunk.Length, left));
            }
            // Entrada depois da grande: o motor precisa achar o cabeçalho local dela passando pelos campos ZIP64.
            using (var s = archive.CreateEntry("depois.txt").Open())
                s.Write(Encoding.UTF8.GetBytes("depois do ZIP64"));
        }

        var info = await _service.InspectAsync(zip, null, ExtractionLimits.Default, CancellationToken.None);
        var big = Assert.Single(info.Entries, e => e.RawKey == "dados/grande.bin");
        Assert.Equal(LargeSize, big.Size);
        Assert.NotNull(big.Crc32);

        var result = await _service.ExtractAsync(new ExtractionRequest { ArchivePath = zip, DestinationDirectory = _tmp.MakeDir("out") },
            new NoConflicts(), null, CancellationToken.None);

        // Completed já prova que o CRC-32 dos 4 GiB conferiu (SafeExtractor recusa CRC divergente).
        Assert.True(result.FinalState == OperationState.Completed, Describe(result));
        File.Delete(zip);
        var extracted = Path.Join(result.Destination!, "dados", "grande.bin");
        Assert.Equal(LargeSize, new FileInfo(extracted).Length);
        Assert.Equal("depois do ZIP64", File.ReadAllText(Path.Join(result.Destination!, "depois.txt")));
        using (var file = File.OpenRead(extracted))
        {
            // Amostras no início, em volta da fronteira de 4 GiB e no fim.
            foreach (var offset in new[] { 0L, uint.MaxValue - 4096L, (long)uint.MaxValue + 1, LargeSize - 5000 })
            {
                var sample = new byte[4096];
                file.Seek(offset, SeekOrigin.Begin);
                file.ReadExactly(sample);
                for (var i = 0; i < sample.Length; i++)
                    Assert.True(sample[i] == pattern[(offset + i) % PatternPeriod], $"byte {offset + i} diferente");
            }
        }
    }

    [Fact]
    [Trait("Category", "Slow")] // fora da CI dos pull requests; roda na release (ver AGENTS.md)
    public async Task Archive_with_more_than_65535_entries_lists_all_and_extracts_entries_past_the_16_bit_limit()
    {
        var zip = _tmp.Sub("muitas.zip");
        using (var stream = File.Create(zip))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            for (var i = 0; i < ManyEntries; i++)
            {
                using var s = archive.CreateEntry($"p{i / 1000:D3}/e{i:D5}.txt", CompressionLevel.NoCompression).Open();
                s.Write(Encoding.UTF8.GetBytes($"entrada {i}"));
            }
        }
        Assert.True(HasZip64EndRecord(zip), "a fixture precisa usar o registro final ZIP64");

        var info = await _service.InspectAsync(zip, null, ExtractionLimits.Default, CancellationToken.None);
        Assert.Equal(ManyEntries, info.Entries.Count(e => !e.IsDirectory));

        // Extrair tudo gravaria 70 mil arquivos (lento no runner); a seleção prova que as entradas além de 65.535 são endereçáveis.
        string[] wanted = ["p000/e00000.txt", "p065/e65535.txt", "p069/e69999.txt"];
        var result = await _service.ExtractAsync(new ExtractionRequest { ArchivePath = zip, DestinationDirectory = _tmp.MakeDir("out"), SelectedPaths = wanted },
            new NoConflicts(), null, CancellationToken.None);

        Assert.True(result.FinalState == OperationState.Completed, Describe(result));
        Assert.Equal(3, result.Items.Count(i => i.Outcome == ItemOutcome.Succeeded));
        Assert.Equal("entrada 0", File.ReadAllText(Path.Join(result.Destination!, "p000", "e00000.txt")));
        Assert.Equal("entrada 65535", File.ReadAllText(Path.Join(result.Destination!, "p065", "e65535.txt")));
        Assert.Equal("entrada 69999", File.ReadAllText(Path.Join(result.Destination!, "p069", "e69999.txt")));
    }

    [Fact]
    public async Task Entry_limit_still_applies_to_zip64_archives()
    {
        var zip = _tmp.Sub("limite.zip");
        using (var stream = File.Create(zip))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            for (var i = 0; i < 66_000; i++)
                archive.CreateEntry($"e{i}.txt", CompressionLevel.NoCompression);
        Assert.True(HasZip64EndRecord(zip));

        var result = await _service.ExtractAsync(new ExtractionRequest
        {
            ArchivePath = zip,
            DestinationDirectory = _tmp.MakeDir("out"),
            Limits = ExtractionLimits.Default with { MaxEntries = 65_536 },
        }, new NoConflicts(), null, CancellationToken.None);

        Assert.Equal(OperationErrorKind.LimitExceeded, result.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_tmp.Sub("out")));
    }
}
