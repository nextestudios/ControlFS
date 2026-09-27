using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Archives;

/// <summary>
/// Compactados divididos em volumes (#65): abrem a partir de qualquer volume, extraem tudo e, faltando um volume,
/// nunca terminam como "concluído". Fixtures em tests/Fixtures (origem no README de lá); cada teste trabalha numa cópia.
/// </summary>
public class VolumeTests : IDisposable
{
    private readonly HashSet<string> _known =
    [
        .. FormatTests.OriginalHashes,
        "13948ac2a7ca9e3bcb926ae4bbd71e8907ec3e9b74356dada87449fc4bc4e0ef", // volumes.txt
        "d589d16fbc74b5dfb3ff58e56d8345790bc3a0a664f7d075a7f952d03bd5e4fd", // docs/leia.txt
    ];

    private readonly TempDir _tmp = new();
    private readonly ArchiveService _service = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class NoConflicts : IExtractionInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("conflito inesperado");
    }

    private Task<OperationResult> Extract(string archive, string destination) =>
        _service.ExtractAsync(new ExtractionRequest { ArchivePath = archive, DestinationDirectory = destination }, new NoConflicts(), null, CancellationToken.None);

    private static string Copy(string dir, string fixture, string? name = null)
    {
        var target = Path.Join(dir, name ?? Path.GetFileName(fixture));
        File.Copy(ZipFixtures.FixturePath(fixture), target);
        return target;
    }

    /// <summary>Uma cópia do conjunto numa pasta própria.</summary>
    private string MakeSet(string set)
    {
        var dir = _tmp.MakeDir(set);
        switch (set)
        {
            case "7z":
                for (var i = 1; i <= 3; i++) Copy(dir, $"7z/volumes.7z.00{i}");
                break;
            case "rar5":
                for (var i = 1; i <= 6; i++) Copy(dir, $"rar/Rar5.multi.part0{i}.rar");
                break;
            case "rar4":
                for (var i = 1; i <= 7; i++) Copy(dir, $"rar/Rar4.multi.part0{i}.rar");
                break;
            case "rar-old": // os mesmos volumes RAR5 com os nomes antigos: .rar, .r00, .r01…
                Copy(dir, "rar/Rar5.multi.part01.rar", "antigo.rar");
                for (var i = 2; i <= 6; i++) Copy(dir, $"rar/Rar5.multi.part0{i}.rar", $"antigo.r{i - 2:D2}");
                break;
            case "zip-spanned":
                Copy(dir, "zip/volumes.z01");
                Copy(dir, "zip/volumes.zip");
                break;
            case "zip-split": // ZIP comum cortado em pedaços (como o 7-Zip faz com -v): dados.zip.001…003
                var random = new Random(65);
                var noise = new byte[3000];
                random.NextBytes(noise);
                var whole = ZipFixtures.Create(Path.Join(_tmp.MakeDir("fonte"), "dados.zip"),
                    new ZipFixtures.Item("ruido.bin", noise), ZipFixtures.Text("docs/leia.txt", "conteúdo do segundo arquivo\n"));
                var bytes = File.ReadAllBytes(whole);
                var cut = bytes.Length / 3;
                File.WriteAllBytes(Path.Join(dir, "dados.zip.001"), bytes[..cut]);
                File.WriteAllBytes(Path.Join(dir, "dados.zip.002"), bytes[cut..(2 * cut)]);
                File.WriteAllBytes(Path.Join(dir, "dados.zip.003"), bytes[(2 * cut)..]);
                _known.Add(Convert.ToHexStringLower(SHA256.HashData(noise)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(set));
        }
        return dir;
    }

    [Theory]
    [InlineData("7z", "volumes.7z.002", ArchiveFormat.SevenZip, "volumes")]
    [InlineData("rar5", "Rar5.multi.part03.rar", ArchiveFormat.Rar, "Rar5.multi")]
    [InlineData("rar4", "Rar4.multi.part07.rar", ArchiveFormat.Rar, "Rar4.multi")]
    [InlineData("rar-old", "antigo.r02", ArchiveFormat.Rar, "antigo")]
    [InlineData("zip-spanned", "volumes.zip", ArchiveFormat.Zip, "volumes")]
    [InlineData("zip-split", "dados.zip.003", ArchiveFormat.Zip, "dados")]
    public async Task Split_archive_opens_from_any_volume_and_extracts_everything(string set, string open, ArchiveFormat format, string folder)
    {
        var path = Path.Join(MakeSet(set), open);
        Assert.Equal(format, _service.Detect(path));

        var info = await _service.InspectAsync(path, null, ExtractionLimits.Default, CancellationToken.None);
        Assert.True(info.IsMultiVolume);
        var files = info.Entries.Count(e => !e.IsDirectory);
        var result = await Extract(path, _tmp.MakeDir("saida-" + set));

        Assert.True(result.FinalState == OperationState.Completed, $"{result.FinalState} {result.Error} {result.Message} :: {string.Join(" | ", result.Items.Where(i => i.Outcome != ItemOutcome.Succeeded).Select(i => $"{i.Name}={i.Outcome}/{i.Error}/{i.Message}"))}");
        Assert.Equal(folder, Path.GetFileName(result.Destination));
        var hashes = Directory.EnumerateFiles(result.Destination!, "*", SearchOption.AllDirectories)
            .Select(f => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)))).ToList();
        Assert.Equal(files, hashes.Count);
        Assert.All(hashes, h => Assert.Contains(h, _known));
    }

    [Theory]
    [InlineData("7z", "volumes.7z.001", "volumes.7z.003", "volumes.7z.001")] // primeiro ausente: o formato vem do nome
    [InlineData("7z", "volumes.7z.003", "volumes.7z.001", "volumes.7z.003")] // último ausente: tamanho declarado no cabeçalho 7z
    [InlineData("rar5", "Rar5.multi.part06.rar", "Rar5.multi.part01.rar", "Rar5.multi.part06.rar")] // RAR5: bloco final diz "há mais"
    [InlineData("rar4", "Rar4.multi.part07.rar", "Rar4.multi.part02.rar", "Rar4.multi.part07.rar")] // RAR4: idem
    [InlineData("zip-spanned", "volumes.z01", "volumes.zip", "volumes.z01")] // ZIP: número do disco no registro final
    [InlineData("zip-spanned", "volumes.zip", "volumes.z01", "volumes.zip")]
    [InlineData("zip-split", "dados.zip.003", "dados.zip.001", "dados.zip.002")] // divisão simples: só o motor percebe
    public async Task Missing_volume_is_reported_by_name_and_nothing_is_extracted(string set, string remove, string open, string named)
    {
        var dir = MakeSet(set);
        File.Delete(Path.Join(dir, remove));
        var path = Path.Join(dir, open);
        Assert.True(ArchiveFormats.CanExtract(_service.Detect(path)), "o volume continua reconhecido como compactado");

        var inspect = await Assert.ThrowsAsync<ArchiveAccessException>(() => _service.InspectAsync(path, null, ExtractionLimits.Default, CancellationToken.None));
        Assert.Equal(OperationErrorKind.MissingVolume, inspect.Kind);
        Assert.Contains(named, inspect.Message, StringComparison.Ordinal);

        var destination = _tmp.MakeDir("saida-" + set);
        var result = await Extract(path, destination);
        Assert.Equal(OperationState.Failed, result.FinalState);
        Assert.Equal(OperationErrorKind.MissingVolume, result.Error);
        Assert.Null(result.Destination);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }

    [Fact]
    public async Task Split_tar_gz_is_refused_instead_of_extracting_only_the_first_part()
    {
        var dir = _tmp.MakeDir("tgz");
        var buffer = new MemoryStream();
        using (var gz = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax))
        {
            var noise = new byte[4000];
            new Random(65).NextBytes(noise);
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "ruido.bin") { DataStream = new MemoryStream(noise) });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "leia.txt") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("fim")) });
        }
        var bytes = buffer.ToArray();
        File.WriteAllBytes(Path.Join(dir, "pacote.tar.gz.001"), bytes[..(bytes.Length / 2)]);
        File.WriteAllBytes(Path.Join(dir, "pacote.tar.gz.002"), bytes[(bytes.Length / 2)..]);
        var destination = _tmp.MakeDir("saida-tgz");

        var result = await Extract(Path.Join(dir, "pacote.tar.gz.001"), destination);

        Assert.Equal(OperationErrorKind.UnsupportedFormat, result.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }
}
