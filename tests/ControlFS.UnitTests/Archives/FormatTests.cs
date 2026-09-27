using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Archives;

/// <summary>
/// Formatos além de ZIP. RAR e 7z usam fixtures do SharpCompress (MIT, ver tests/Fixtures/README.md) que contêm os
/// mesmos três arquivos originais; cada arquivo extraído precisa ter o SHA-256 de um desses originais.
/// </summary>
public class FormatTests : IDisposable
{
    internal static readonly HashSet<string> OriginalHashes =
    [
        "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106", // exe/test.exe
        "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53", // jpg/test.jpg
        // тест.txt: os compactados guardam a versão com CRLF; no repositório do SharpCompress o original está com LF
        // (1a3de4e5…), então comparamos com o hash da versão CRLF, que é o conteúdo exato arquivado.
        "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9", // тест.txt (CRLF)
    ];

    private readonly TempDir _tmp = new();
    private readonly ArchiveService _service = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class NoConflicts : IExtractionInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("conflito inesperado");
    }

    private Task<OperationResult> Extract(string archive, string? password = null, IReadOnlyCollection<string>? selected = null) =>
        _service.ExtractAsync(new ExtractionRequest { ArchivePath = archive, DestinationDirectory = _tmp.MakeDir("out-" + Guid.NewGuid().ToString("N")), Password = password, SelectedPaths = selected },
            new NoConflicts(), null, CancellationToken.None);

    private static IReadOnlyList<string> HashesUnder(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)))).ToList();

    [Theory]
    [InlineData("rar/Rar4.rar", ArchiveFormat.Rar)]
    [InlineData("rar/Rar5.rar", ArchiveFormat.Rar)]
    [InlineData("rar/Rar.solid.rar", ArchiveFormat.Rar)]
    [InlineData("rar/Rar5.solid.rar", ArchiveFormat.Rar)]
    [InlineData("7z/7Zip.LZMA2.7z", ArchiveFormat.SevenZip)]
    [InlineData("7z/7Zip.solid.7z", ArchiveFormat.SevenZip)]
    public async Task Extracts_rar_and_7z_fixtures_with_correct_content(string fixture, ArchiveFormat format)
    {
        var path = ZipFixtures.FixturePath(fixture);
        Assert.Equal(format, _service.Detect(path));

        var result = await Extract(path);

        Assert.True(result.FinalState == OperationState.Completed, $"{result.FinalState} {result.Error} {result.Message} :: {string.Join(" | ", result.Items.Where(i => i.Outcome != ItemOutcome.Succeeded).Select(i => $"{i.Name}={i.Outcome}/{i.Error}/{i.Message}"))}");
        var hashes = HashesUnder(result.Destination!);
        Assert.NotEmpty(hashes);
        Assert.All(hashes, h => Assert.Contains(h, OriginalHashes));
        Assert.Superset(OriginalHashes, hashes.ToHashSet());
        Assert.Equal(OriginalHashes.Count, hashes.Distinct().Count());
    }

    [Fact]
    public async Task Rar5_with_encrypted_files_requires_the_right_password()
    {
        var path = ZipFixtures.FixturePath("rar/Rar5.encrypted_filesOnly.rar");
        var missing = await Extract(path);
        Assert.Equal(OperationErrorKind.PasswordRequired, missing.Error);
        var wrong = await Extract(path, "errada");
        Assert.Contains(wrong.Error, new[] { OperationErrorKind.WrongPassword, OperationErrorKind.WrongPasswordOrCorrupt });
        Assert.NotEqual(OperationState.Completed, wrong.FinalState);
        var ok = await Extract(path, "test");
        Assert.True(ok.FinalState == OperationState.Completed, $"{ok.FinalState} :: {string.Join(" | ", ok.Items.Where(i => i.Outcome != ItemOutcome.Succeeded).Select(i => $"{i.Name}={i.Outcome}/{i.Error}/{i.Message}"))}");
        Assert.All(HashesUnder(ok.Destination!), h => Assert.Contains(h, OriginalHashes));
    }

    [Fact]
    public async Task SevenZip_aes_extracts_with_password_and_asks_without_it()
    {
        var path = ZipFixtures.FixturePath("7z/7Zip.LZMA2.Aes.7z");
        var missing = await Extract(path);
        Assert.Equal(OperationErrorKind.PasswordRequired, missing.Error);
        var ok = await Extract(path, "testpassword");
        Assert.True(ok.FinalState == OperationState.Completed, $"{ok.FinalState} {ok.Error} {ok.Message}");
        Assert.All(HashesUnder(ok.Destination!), h => Assert.Contains(h, OriginalHashes));
    }

    [Theory]
    [InlineData("rar/Rar5.encrypted_filesAndHeader.rar", "test")]
    [InlineData("7z/cabecalho-protegido.7z", "certa")]
    public async Task Header_encrypted_archive_lists_and_extracts_only_with_the_right_password(string fixture, string password)
    {
        // A própria lista de arquivos é criptografada: sem a senha certa nem os nomes podem ser lidos.
        HashSet<string> known =
        [
            .. OriginalHashes,
            "1d23c69cf6c61f433b6470152d59a3cec7a5212f9b338c89a15373a679d0a67d", // segredo.txt ("conteúdo protegido\n")
            "150f2a4850496ec22fce5335e5d56f1ffef5eefd0816dfd0af905a531b24fb6f", // docs/nota.txt ("nota\n")
        ];
        var path = ZipFixtures.FixturePath(fixture);
        var limits = ControlFS.Core.Policies.ExtractionLimits.Default;

        var missing = await Assert.ThrowsAsync<ArchiveAccessException>(() => _service.InspectAsync(path, null, limits, CancellationToken.None));
        Assert.Equal(OperationErrorKind.PasswordRequired, missing.Kind);
        var wrong = await Assert.ThrowsAsync<ArchiveAccessException>(() => _service.InspectAsync(path, "errada", limits, CancellationToken.None));
        Assert.Equal(OperationErrorKind.WrongPassword, wrong.Kind);
        Assert.Equal(OperationErrorKind.WrongPassword, (await Extract(path, "errada")).Error);

        var info = await _service.InspectAsync(path, password, limits, CancellationToken.None);
        var files = info.Entries.Where(e => !e.IsDirectory).ToList();
        Assert.NotEmpty(files);
        Assert.All(files, e => Assert.True(e.IsEncrypted, e.RawKey));
        var ok = await Extract(path, password);
        Assert.True(ok.FinalState == OperationState.Completed, $"{ok.FinalState} {ok.Error} {ok.Message} :: {string.Join(" | ", ok.Items.Where(i => i.Outcome != ItemOutcome.Succeeded).Select(i => $"{i.Name}={i.Outcome}/{i.Error}/{i.Message}"))}");
        var hashes = HashesUnder(ok.Destination!);
        Assert.Equal(files.Count, hashes.Count);
        Assert.All(hashes, h => Assert.Contains(h, known));
    }

    [Fact]
    public async Task Winzip_aes256_ae2_fixture_needs_the_right_password_and_extracts_identical_files()
    {
        // Gerado por ferramenta real (WinZip AES, AE-2, AES-256): CRC 0, então só o conteúdo byte a byte prova a extração.
        var path = ZipFixtures.FixturePath("zip/Zip.deflate.WinzipAES.zip");
        var info = await _service.InspectAsync(path, null, ControlFS.Core.Policies.ExtractionLimits.Default, CancellationToken.None);
        Assert.True(info.HasEncryptedEntries);

        var missing = await Extract(path);
        Assert.Equal(OperationErrorKind.PasswordRequired, missing.Error);
        var wrong = await Extract(path, "errada");
        Assert.Equal(OperationErrorKind.WrongPassword, wrong.Error);
        Assert.Null(wrong.Destination);
        var ok = await Extract(path, "test");
        Assert.True(ok.FinalState == OperationState.Completed, $"{ok.FinalState} {ok.Error} {ok.Message} :: {string.Join(" | ", ok.Items.Where(i => i.Outcome != ItemOutcome.Succeeded).Select(i => $"{i.Name}={i.Outcome}/{i.Error}/{i.Message}"))}");
        var hashes = HashesUnder(ok.Destination!);
        Assert.Equal(OriginalHashes.Count, hashes.Count);
        Assert.All(hashes, h => Assert.Contains(h, OriginalHashes));
    }

    [Theory]
    [InlineData(1, 128)] // AE-1: CRC guardado e conferido
    [InlineData(2, 192)] // AE-2: CRC 0, verificação CRC pulada
    public async Task Generated_winzip_aes_zip_missing_wrong_and_correct_password(int aeVersion, int keyBits)
    {
        var path = AesZipFixtures.Create(_tmp.Sub($"aes{keyBits}.zip"), "c3rta", new AesZipFixtures.Options(aeVersion, keyBits),
            ("segredo.txt", Encoding.UTF8.GetBytes("conteúdo protegido")), ("docs/nota.txt", Encoding.UTF8.GetBytes(new string('n', 5000))));

        Assert.Equal(OperationErrorKind.PasswordRequired, (await Extract(path)).Error);
        Assert.Equal(OperationErrorKind.WrongPassword, (await Extract(path, "errada")).Error);
        var ok = await Extract(path, "c3rta");
        Assert.True(ok.FinalState == OperationState.Completed, $"{ok.FinalState} {ok.Error} {ok.Message} :: {string.Join(" | ", ok.Items.Select(i => $"{i.Name}={i.Outcome}/{i.Error}/{i.Message}"))}");
        Assert.Equal("conteúdo protegido", File.ReadAllText(Path.Join(ok.Destination!, "segredo.txt")));
        Assert.Equal(new string('n', 5000), File.ReadAllText(Path.Join(ok.Destination!, "docs", "nota.txt")));
    }

    [Fact]
    public async Task Tampered_aes_ae1_entry_is_rejected_by_crc_and_not_placed()
    {
        var path = AesZipFixtures.Create(_tmp.Sub("adulterado.zip"), "c3rta", new AesZipFixtures.Options(1, 256, Deflate: false, CorruptPayload: true),
            ("dados.txt", Encoding.UTF8.GetBytes("dados que serão adulterados")));

        var result = await Extract(path, "c3rta");

        Assert.NotEqual(OperationState.Completed, result.FinalState);
        var item = Assert.Single(result.Items);
        Assert.Equal(OperationErrorKind.WrongPasswordOrCorrupt, item.Error);
        Assert.Null(result.Destination);
    }

    private string MakeTar(string path, bool gzip)
    {
        using var file = File.Create(path);
        using Stream outer = gzip ? new GZipStream(file, CompressionLevel.Optimal) : file;
        using var tar = new TarWriter(outer, TarEntryFormat.Pax);
        tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "docs/"));
        foreach (var (name, content) in new[] { ("docs/a.txt", "alfa"), ("docs/sub/ação.txt", "beta"), ("raiz.txt", "raiz") })
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
        tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "/etc/passwd" });
        tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "../fora.txt") { DataStream = new MemoryStream("x"u8.ToArray()) });
        return path;
    }

    [Theory]
    [InlineData(false, ArchiveFormat.Tar)]
    [InlineData(true, ArchiveFormat.TarGZip)]
    public async Task Tar_and_tar_gz_extract_safely(bool gzip, ArchiveFormat format)
    {
        var path = MakeTar(_tmp.Sub(gzip ? "pacote.tar.gz" : "pacote.tar"), gzip);
        Assert.Equal(format, _service.Detect(path));

        var result = await Extract(path);

        Assert.True(result.FinalState == OperationState.CompletedWithWarnings, // link e ../ bloqueados
            $"{result.FinalState} {result.Error} {result.Message} :: {string.Join(" | ", result.Items.Select(i => $"{i.Name}={i.Outcome}/{i.Error}"))}");
        Assert.Equal("pacote", Path.GetFileName(result.Destination));
        Assert.Equal("alfa", File.ReadAllText(Path.Join(result.Destination!, "docs", "a.txt")));
        Assert.Equal("beta", File.ReadAllText(Path.Join(result.Destination!, "docs", "sub", "ação.txt")));
        Assert.Equal("raiz", File.ReadAllText(Path.Join(result.Destination!, "raiz.txt")));
        Assert.Contains(result.Items, i => i.Name == "link" && i.Error == OperationErrorKind.LinkOrSpecialBlocked);
        Assert.Contains(result.Items, i => i.Error == OperationErrorKind.PathRejected);
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(result.Destination!, "*", SearchOption.AllDirectories), p => p.Contains("PaxHeader", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Join(Path.GetDirectoryName(result.Destination!)!, "fora.txt")));
    }

    [Fact]
    public async Task Tar_gz_selection_is_extracted_in_a_single_sequential_pass()
    {
        var path = MakeTar(_tmp.Sub("sel.tgz"), gzip: true);
        var info = await _service.InspectAsync(path, null, ControlFS.Core.Policies.ExtractionLimits.Default, CancellationToken.None);
        Assert.Equal(ArchiveFormat.TarGZip, info.Format);
        Assert.Contains(info.Entries, e => e.RawKey == "docs/sub/ação.txt");

        var result = await Extract(path, selected: ["docs/sub"]);

        Assert.True(result.FinalState == OperationState.Completed, $"{result.FinalState} {result.Error} {result.Message} :: {string.Join(" | ", result.Items.Select(i => $"{i.Name}={i.Outcome}/{i.Error}/{i.Message}"))}");
        var files = Directory.EnumerateFiles(result.Destination!, "*", SearchOption.AllDirectories).ToList();
        Assert.Equal(["ação.txt"], files.Select(Path.GetFileName));
    }

    [Fact]
    public async Task Gz_single_file_is_decompressed_as_one_file()
    {
        var path = _tmp.Sub("notas.txt.gz");
        using (var file = File.Create(path))
        using (var gz = new GZipStream(file, CompressionLevel.Optimal))
            gz.Write(Encoding.UTF8.GetBytes("conteúdo comprimido"));
        Assert.Equal(ArchiveFormat.GZip, _service.Detect(path));

        var result = await Extract(path);

        Assert.True(result.FinalState == OperationState.Completed, $"{result.FinalState} {result.Error} {result.Message} :: {string.Join(" | ", result.Items.Select(i => $"{i.Name}={i.Outcome}/{i.Error}/{i.Message}"))}");
        var single = Assert.Single(Directory.EnumerateFiles(result.Destination!, "*", SearchOption.AllDirectories));
        Assert.Equal("notas.txt", Path.GetFileName(single));
        Assert.Equal("conteúdo comprimido", File.ReadAllText(single));
    }

    [Theory]
    [InlineData("fotos.tar.gz", "fotos")]
    [InlineData("dados.TGZ", "dados")]
    [InlineData("a.b.zip", "a.b")]
    [InlineData("notas.txt.gz", "notas.txt")]
    [InlineData("sem-extensao", "sem-extensao")]
    public void Stem_strips_archive_extensions(string name, string stem) => Assert.Equal(stem, ArchiveFormats.StemOf(name));
}
