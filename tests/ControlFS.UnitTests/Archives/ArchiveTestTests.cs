using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Archives;

/// <summary>Teste de integridade (#66): lê tudo, confere tamanho/CRC e nunca grava nada em disco.</summary>
public class ArchiveTestTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly ArchiveService _service = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class SyncProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }

    private Task<OperationResult> Test(string path, IProgress<OperationProgress>? progress = null, CancellationToken ct = default) =>
        _service.TestAsync(new ArchiveTestRequest { ArchivePath = path }, progress, ct);

    private string CorruptedZip()
    {
        var zip = Create(_tmp.Sub("baixado.zip"), Text("ok.txt", "intacto"),
            new Item("dados.txt", Encoding.ASCII.GetBytes("CONTEUDO-ORIGINAL-1234567890"), Level: CompressionLevel.NoCompression));
        var bytes = File.ReadAllBytes(zip);
        var offset = bytes.AsSpan().IndexOf("ORIGINAL"u8);
        bytes[offset] ^= 0x20;
        File.WriteAllBytes(zip, bytes);
        return zip;
    }

    [Fact]
    public async Task Corrupted_entry_is_reported_by_name_and_nothing_is_written()
    {
        var zip = CorruptedZip();
        var before = _tmp.Snapshot();

        var result = await Test(zip);

        Assert.Equal(OperationState.CompletedWithWarnings, result.FinalState);
        var failed = Assert.Single(result.Items, i => i.Outcome == ItemOutcome.Failed);
        Assert.Equal("dados.txt", failed.Name);
        Assert.Equal(OperationErrorKind.Corrupt, failed.Error);
        var ok = Assert.Single(result.Items, i => i.Outcome == ItemOutcome.Succeeded);
        Assert.Equal("ok.txt", ok.Name);
        Assert.False(ok.NoChecksum);
        Assert.Equal(before, _tmp.Snapshot()); // nem staging, nem pasta dedicada
    }

    [Fact]
    public async Task Good_zip_reports_ok_and_tar_reports_entries_without_checksum()
    {
        var zip = Create(_tmp.Sub("bom.zip"), Text("a.txt", "alfa"), Dir("sub"), Text("sub/b.txt", "beta"));
        var good = await Test(zip);
        Assert.Equal(OperationState.Completed, good.FinalState);
        Assert.Equal(2, good.Items.Count);
        Assert.All(good.Items, i => Assert.False(i.NoChecksum));

        var tar = _tmp.Sub("pacote.tar");
        using (var file = File.Create(tar))
        using (var writer = new TarWriter(file, TarEntryFormat.Pax))
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "x.txt") { DataStream = new MemoryStream("xis"u8.ToArray()) });
        var tarResult = await Test(tar);
        Assert.Equal(OperationState.Completed, tarResult.FinalState);
        Assert.True(Assert.Single(tarResult.Items).NoChecksum);
    }

    [Fact]
    public async Task Cancelling_during_progress_stops_and_marks_the_rest_not_processed()
    {
        var zip = Create(_tmp.Sub("varios.zip"), Enumerable.Range(0, 20).Select(i => Text($"f{i:D2}.txt", new string('x', 100))).ToArray());
        using var cts = new CancellationTokenSource();
        var reports = new List<OperationProgress>();
        var progress = new SyncProgress(p =>
        {
            reports.Add(p);
            if (p.ItemsProcessed == 3) cts.Cancel();
        });

        var result = await Test(zip, progress, cts.Token);

        Assert.Equal(OperationState.Cancelled, result.FinalState);
        Assert.Equal(20, reports[0].ItemsTotal);
        Assert.InRange(result.Count(ItemOutcome.Succeeded), 3, 4);
        Assert.Equal(20, result.Items.Count);
        Assert.Contains(result.Items, i => i.Outcome == ItemOutcome.NotProcessed);
    }
}
