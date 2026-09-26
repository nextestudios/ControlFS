using System.IO.Compression;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Archives;

public class SafeExtractorTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly ArchiveService _service = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class ScriptedInteraction(params ConflictDecision[] decisions) : IExtractionInteraction
    {
        private readonly Queue<ConflictDecision> _decisions = new(decisions);
        public List<ConflictInfo> Seen { get; } = [];

        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken)
        {
            Seen.Add(conflict);
            return Task.FromResult(_decisions.Count > 0 ? _decisions.Dequeue() : throw new InvalidOperationException("Conflito inesperado."));
        }
    }

    private Task<OperationResult> Extract(string zip, string dest, DestinationMode mode = DestinationMode.CreateDedicatedFolder,
        IExtractionInteraction? interaction = null, string? password = null, ExtractionLimits? limits = null,
        IReadOnlyCollection<string>? selected = null, string basePath = "", CancellationToken ct = default) =>
        _service.ExtractAsync(new ExtractionRequest
        {
            ArchivePath = zip,
            DestinationDirectory = dest,
            Mode = mode,
            Password = password,
            Limits = limits ?? ExtractionLimits.Default,
            SelectedPaths = selected,
            BaseInnerPath = basePath,
        }, interaction ?? new ScriptedInteraction(), null, ct);

    [Fact]
    public async Task Extracts_simple_zip_into_dedicated_folder_and_removes_staging()
    {
        var zip = Create(_tmp.Sub("fotos.zip"), Text("a.txt", "alfa"), Dir("sub"), Text("sub/b.txt", "beta"), Text("Relatórios/ação.txt", "ç"));
        var dest = _tmp.MakeDir("out");
        var result = await Extract(zip, dest);

        Assert.Equal(OperationState.Completed, result.FinalState);
        Assert.Equal(Path.Join(dest, "fotos"), result.Destination);
        Assert.Equal("alfa", File.ReadAllText(Path.Join(dest, "fotos", "a.txt")));
        Assert.Equal("beta", File.ReadAllText(Path.Join(dest, "fotos", "sub", "b.txt")));
        Assert.Equal("ç", File.ReadAllText(Path.Join(dest, "fotos", "Relatórios", "ação.txt")));
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(Path.Join(dest, "fotos"), "*", SearchOption.AllDirectories),
            p => p.Contains(".controlfs-", StringComparison.Ordinal));
        Assert.True(File.Exists(zip), "o compactado original nunca é apagado");
    }

    [Fact]
    public async Task Dedicated_folder_never_reuses_an_existing_one()
    {
        var zip = Create(_tmp.Sub("pack.zip"), Text("a.txt", "novo"));
        var dest = _tmp.MakeDir("out");
        Directory.CreateDirectory(Path.Join(dest, "pack"));
        File.WriteAllText(Path.Join(dest, "pack", "a.txt"), "antigo");

        var result = await Extract(zip, dest);

        Assert.Equal(Path.Join(dest, "pack (2)"), result.Destination);
        Assert.Equal("antigo", File.ReadAllText(Path.Join(dest, "pack", "a.txt")));
        Assert.Equal("novo", File.ReadAllText(Path.Join(dest, "pack (2)", "a.txt")));
    }

    [Fact]
    public async Task Malicious_paths_never_write_outside_destination()
    {
        var zip = Create(_tmp.Sub("evil.zip"),
            Text("../escape.txt", "x"),
            Text("a/../../escape2.txt", "x"),
            Text("/abs.txt", "x"),
            Text("C:\\win.txt", "x"),
            Text("C:drive-relative.txt", "x"),
            Text("\\\\server\\share\\unc.txt", "x"),
            Text("\\\\?\\C:\\device.txt", "x"),
            Text("ok.txt:Zone.Identifier", "x"),
            Text("CON.txt", "x"),
            Text("trail. ", "x"),
            Text("ok/fine.txt", "ok"));
        var dest = _tmp.MakeDir("out");
        var before = _tmp.Snapshot();

        var result = await Extract(zip, dest);

        Assert.Equal(OperationState.CompletedWithWarnings, result.FinalState);
        Assert.Equal(10, result.Count(ItemOutcome.Blocked));
        Assert.All(result.Items.Where(i => i.Outcome == ItemOutcome.Blocked), i => Assert.Equal(OperationErrorKind.PathRejected, i.Error));
        Assert.Equal("ok", File.ReadAllText(Path.Join(dest, "evil", "ok", "fine.txt")));
        var created = _tmp.Snapshot().Except(before).ToList();
        Assert.All(created, p => Assert.StartsWith("out/evil", p, StringComparison.Ordinal));
        Assert.Equal(["out/evil", "out/evil/ok", "out/evil/ok/fine.txt"], created);
    }

    [Fact]
    public async Task Symlink_entries_are_blocked()
    {
        var zip = Create(_tmp.Sub("links.zip"), Symlink("link", "/etc/passwd"), Text("real.txt", "r"));
        var dest = _tmp.MakeDir("out");

        var result = await Extract(zip, dest);

        var blocked = Assert.Single(result.Items, i => i.Outcome == ItemOutcome.Blocked);
        Assert.Equal(OperationErrorKind.LinkOrSpecialBlocked, blocked.Error);
        Assert.False(File.Exists(Path.Join(dest, "links", "link")));
        Assert.True(File.Exists(Path.Join(dest, "links", "real.txt")));
    }

    [Fact]
    public async Task Existing_link_in_destination_is_not_followed()
    {
        var outside = _tmp.MakeDir("outside");
        var dest = _tmp.MakeDir("out");
        try { Directory.CreateSymbolicLink(Path.Join(dest, "docs"), outside); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Sem privilégio para criar symlink neste ambiente (Windows sem modo desenvolvedor).");
        }
        var zip = Create(_tmp.Sub("x.zip"), Text("docs/evil.txt", "x"), Text("safe.txt", "s"));

        var result = await Extract(zip, dest, DestinationMode.IntoExistingFolder);

        var blocked = Assert.Single(result.Items, i => i.Name == "docs/evil.txt");
        Assert.Equal(ItemOutcome.Blocked, blocked.Outcome);
        Assert.Equal(OperationErrorKind.DestinationTraversesLink, blocked.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.True(File.Exists(Path.Join(dest, "safe.txt")));
    }

    [Fact]
    public async Task Case_and_type_collisions_are_rejected_not_merged()
    {
        var zip = Create(_tmp.Sub("c.zip"), Text("Leia.txt", "1"), Text("leia.TXT", "2"), Text("x", "file"), Text("x/y.txt", "inside"),
            Text("ação.txt", "nfc"), Text("ac\u0327a\u0303o.txt", "nfd"));
        var dest = _tmp.MakeDir("out");

        var result = await Extract(zip, dest);

        var collisions = result.Items.Where(i => i.Error == OperationErrorKind.NameCollision).Select(i => i.Name).ToList();
        Assert.Equal(["leia.TXT", "x/y.txt", "ac\u0327a\u0303o.txt"], collisions);
        Assert.Equal("1", File.ReadAllText(Path.Join(dest, "c", "Leia.txt")));
        Assert.Equal("file", File.ReadAllText(Path.Join(dest, "c", "x")));
    }

    [Fact]
    public async Task Conflicts_skip_keep_both_and_replace_follow_user_choice()
    {
        var zip = Create(_tmp.Sub("c.zip"), Text("a.txt", "NOVO-a"), Text("b.txt", "NOVO-b"), Text("c.txt", "NOVO-c"));
        var dest = _tmp.MakeDir("out");
        foreach (var n in new[] { "a.txt", "b.txt", "c.txt" }) File.WriteAllText(Path.Join(dest, n), "velho");
        var interaction = new ScriptedInteraction(
            new ConflictDecision(ConflictChoice.Skip),
            new ConflictDecision(ConflictChoice.KeepBoth),
            new ConflictDecision(ConflictChoice.Replace));

        var result = await Extract(zip, dest, DestinationMode.IntoExistingFolder, interaction);

        Assert.Equal(3, interaction.Seen.Count);
        Assert.Equal(Path.Join(dest, "a.txt"), interaction.Seen[0].ExistingPath);
        Assert.Equal(5, interaction.Seen[0].ExistingSize);
        Assert.Equal("velho", File.ReadAllText(Path.Join(dest, "a.txt")));
        Assert.Equal("velho", File.ReadAllText(Path.Join(dest, "b.txt")));
        Assert.Equal("NOVO-b", File.ReadAllText(Path.Join(dest, "b (2).txt")));
        Assert.Equal("NOVO-c", File.ReadAllText(Path.Join(dest, "c.txt")));
        Assert.Equal([ItemOutcome.Skipped, ItemOutcome.Renamed, ItemOutcome.Replaced], result.Items.Select(i => i.Outcome));
        Assert.Equal(OperationState.Completed, result.FinalState);
    }

    [Fact]
    public async Task Apply_to_remaining_is_scoped_to_the_operation()
    {
        var zip = Create(_tmp.Sub("c.zip"), Text("a.txt", "N"), Text("b.txt", "N"), Text("c.txt", "N"));
        var dest = _tmp.MakeDir("out");
        foreach (var n in new[] { "a.txt", "b.txt", "c.txt" }) File.WriteAllText(Path.Join(dest, n), "velho");

        var first = new ScriptedInteraction(new ConflictDecision(ConflictChoice.KeepBoth, ApplyToRemaining: true));
        await Extract(zip, dest, DestinationMode.IntoExistingFolder, first);
        Assert.Single(first.Seen);
        Assert.True(File.Exists(Path.Join(dest, "c (2).txt")));

        // Nova operação: a decisão anterior não vale mais.
        var second = new ScriptedInteraction(new ConflictDecision(ConflictChoice.Skip), new ConflictDecision(ConflictChoice.Skip), new ConflictDecision(ConflictChoice.Skip));
        await Extract(zip, dest, DestinationMode.IntoExistingFolder, second);
        Assert.Equal(3, second.Seen.Count);
    }

    [Fact]
    public async Task Cancel_at_conflict_keeps_completed_files_and_reports_the_rest()
    {
        var zip = Create(_tmp.Sub("c.zip"), Text("1.txt", "um"), Text("2.txt", "dois"), Text("3.txt", "tres"));
        var dest = _tmp.MakeDir("out");
        File.WriteAllText(Path.Join(dest, "2.txt"), "velho");

        var result = await Extract(zip, dest, DestinationMode.IntoExistingFolder, new ScriptedInteraction(new ConflictDecision(ConflictChoice.Cancel)));

        Assert.Equal(OperationState.Cancelled, result.FinalState);
        Assert.Equal("um", File.ReadAllText(Path.Join(dest, "1.txt")));
        Assert.Equal("velho", File.ReadAllText(Path.Join(dest, "2.txt")));
        Assert.False(File.Exists(Path.Join(dest, "3.txt")));
        Assert.Equal(ItemOutcome.NotProcessed, result.Items.Single(i => i.Name == "3.txt").Outcome);
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(dest), p => p.Contains(".controlfs-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Size_limits_count_actual_bytes()
    {
        var zip = Create(_tmp.Sub("big.zip"), new Item("zeros.bin", new byte[200_000]));
        var dest = _tmp.MakeDir("out");

        var result = await Extract(zip, dest, limits: new ExtractionLimits { MaxEntryBytes = 100_000 });

        Assert.Equal(OperationState.Failed, result.FinalState);
        Assert.Equal(OperationErrorKind.LimitExceeded, result.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dest)); // pasta dedicada vazia removida
    }

    [Fact]
    public async Task Expansion_ratio_is_an_additional_signal()
    {
        var zip = Create(_tmp.Sub("bomb.zip"), new Item("zeros.bin", new byte[4_000_000], Level: CompressionLevel.SmallestSize));
        var dest = _tmp.MakeDir("out");

        var result = await Extract(zip, dest, limits: new ExtractionLimits { MaxCompressionRatio = 50, RatioCheckThresholdBytes = 1_000_000 });

        Assert.Equal(OperationErrorKind.LimitExceeded, result.Error);
    }

    [Fact]
    public async Task Entry_count_limit_applies_to_metadata()
    {
        var zip = Create(_tmp.Sub("many.zip"), Enumerable.Range(0, 30).Select(i => Text($"f{i}.txt", "x")).ToArray());
        var result = await Extract(zip, _tmp.MakeDir("out"), limits: new ExtractionLimits { MaxEntries = 10 });
        Assert.Equal(OperationErrorKind.LimitExceeded, result.Error);
    }

    [Fact]
    public async Task Corrupted_payload_fails_crc_and_leaves_no_partial_file()
    {
        var zip = Create(_tmp.Sub("crc.zip"), new Item("dados.txt", Encoding.ASCII.GetBytes("CONTEUDO-ORIGINAL-1234567890"), Level: CompressionLevel.NoCompression));
        var bytes = File.ReadAllBytes(zip);
        var offset = IndexOf(bytes, Encoding.ASCII.GetBytes("ORIGINAL"));
        bytes[offset] ^= 0x20;
        File.WriteAllBytes(zip, bytes);
        var dest = _tmp.MakeDir("out");

        var result = await Extract(zip, dest, DestinationMode.IntoExistingFolder);

        var item = Assert.Single(result.Items);
        Assert.Equal(ItemOutcome.Failed, item.Outcome);
        Assert.Equal(OperationErrorKind.Corrupt, item.Error);
        Assert.False(File.Exists(Path.Join(dest, "dados.txt")));
        Assert.Equal(OperationState.CompletedWithWarnings, result.FinalState);
    }

    [Fact]
    public async Task Truncated_archive_is_reported_as_corrupt()
    {
        var zip = Create(_tmp.Sub("t.zip"), Text("a.txt", "abc"));
        var bytes = File.ReadAllBytes(zip);
        File.WriteAllBytes(zip, bytes[..^30]);
        var result = await Extract(zip, _tmp.MakeDir("out"));
        Assert.Equal(OperationState.Failed, result.FinalState);
        Assert.Equal(OperationErrorKind.Corrupt, result.Error);
    }

    [Fact]
    public async Task Zipcrypto_password_missing_wrong_and_correct()
    {
        var zip = FixturePath("zip/zipcrypto-senha-certa.zip");
        var dest = _tmp.MakeDir("out");

        var missing = await Extract(zip, dest);
        Assert.Equal(OperationErrorKind.PasswordRequired, missing.Error);

        var wrong = await Extract(zip, dest, password: "errada");
        Assert.Equal(OperationErrorKind.WrongPassword, wrong.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dest));

        var ok = await Extract(zip, dest, password: "certa");
        Assert.Equal(OperationState.Completed, ok.FinalState);
        Assert.Equal("conteúdo protegido\n", File.ReadAllText(Path.Join(ok.Destination!, "segredo.txt")));
        Assert.Equal("nota\n", File.ReadAllText(Path.Join(ok.Destination!, "docs", "nota.txt")));
    }

    [Fact]
    public async Task Failed_extraction_leaves_no_empty_folder_tree_behind()
    {
        // Diretórios antes do primeiro arquivo: a pasta dedicada chega a ganhar subpastas antes da falha.
        var zip = Create(_tmp.Sub("pack.zip"), Dir("a"), Dir("a/b"), new Item("a/b/zeros.bin", new byte[50_000]));
        var dest = _tmp.MakeDir("out");
        var result = await Extract(zip, dest, limits: new ExtractionLimits { MaxEntryBytes = 10_000 });
        Assert.Equal(OperationErrorKind.LimitExceeded, result.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dest));
    }

    [Fact]
    public async Task Selection_from_subfolder_strips_base_path()
    {
        var zip = Create(_tmp.Sub("s.zip"), Text("top.txt", "t"), Text("docs/a.txt", "a"), Text("docs/img/b.png", "b"), Text("docs/c.txt", "c"));
        var dest = _tmp.MakeDir("out");

        var result = await Extract(zip, dest, DestinationMode.IntoExistingFolder, selected: ["docs/a.txt", "docs/img"], basePath: "docs");

        Assert.Equal(2, result.Count(ItemOutcome.Succeeded));
        Assert.True(File.Exists(Path.Join(dest, "a.txt")));
        Assert.True(File.Exists(Path.Join(dest, "img", "b.png")));
        Assert.False(File.Exists(Path.Join(dest, "top.txt")));
        Assert.False(File.Exists(Path.Join(dest, "c.txt")));
    }

    [Fact]
    public async Task Cancellation_before_start_writes_nothing()
    {
        var zip = Create(_tmp.Sub("a.zip"), Text("a.txt", "a"));
        var dest = _tmp.MakeDir("out");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await Extract(zip, dest, ct: cts.Token);
        Assert.Equal(OperationState.Cancelled, result.FinalState);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dest));
    }

    [Fact]
    public async Task Detection_uses_content_not_extension()
    {
        var fake7z = _tmp.Sub("x.7z");
        File.WriteAllBytes(fake7z, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0, 4, 1, 2, 3]);
        var renamedZip = Create(_tmp.Sub("na-verdade-zip.dat"), Text("a.txt", "a"));
        var notAnArchive = _tmp.Sub("foto.zip");
        File.WriteAllText(notAnArchive, "não sou um zip");

        Assert.Equal(ArchiveFormat.SevenZip, _service.Detect(fake7z));
        Assert.Equal(ArchiveFormat.Zip, _service.Detect(renamedZip)); // conteúdo decide, não a extensão
        Assert.Equal(ArchiveFormat.Unknown, _service.Detect(notAnArchive));
        var truncated = await Extract(fake7z, _tmp.MakeDir("out"));
        Assert.Equal(OperationState.Failed, truncated.FinalState); // assinatura válida, conteúdo inválido: falha sem falso sucesso
        var unknown = await Extract(notAnArchive, _tmp.MakeDir("out2"));
        Assert.Equal(OperationErrorKind.UnsupportedFormat, unknown.Error);
    }

    [Fact]
    public async Task Inspection_lists_entries_without_extracting()
    {
        var zip = Create(_tmp.Sub("i.zip"), Text("a.txt", "abc"), Text("d/b.txt", "defg"));
        var info = await _service.InspectAsync(zip, null, ExtractionLimits.Default, CancellationToken.None);
        Assert.Equal(ArchiveFormat.Zip, info.Format);
        Assert.Equal(["a.txt", "d/b.txt"], info.Entries.Select(e => e.RawKey));
        Assert.Equal(7, info.DeclaredTotalSize);
        Assert.Equal(["i.zip"], Directory.EnumerateFileSystemEntries(_tmp.Path).Select(Path.GetFileName));
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidOperationException("padrão não encontrado");
    }
}
