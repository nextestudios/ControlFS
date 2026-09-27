using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Application;

/// <summary>Extrair vários compactados marcados de uma vez, cada um na sua pasta (#69).</summary>
public class BatchExtractionJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Three_marked_archives_go_to_three_dedicated_folders_with_their_own_results() => UiContext.Run(async () =>
    {
        // Mesmo nome de arquivo dentro de todos e dois compactados com o mesmo radical: nada pode se misturar.
        Create(_tmp.Sub("docs.zip"), Text("leia.txt", "docs"));
        Create(_tmp.Sub("fotos.zip"), Text("leia.txt", "fotos-zip"));
        using (var file = File.Create(_tmp.Sub("fotos.tar.gz")))
        using (var gz = new GZipStream(file, CompressionLevel.Optimal))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "leia.txt") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("fotos-tgz")) });
        File.WriteAllText(_tmp.Sub("notas.txt"), "não é compactado");

        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        foreach (var name in new[] { "docs.zip", "fotos.tar.gz", "fotos.zip", "notas.txt" })
        {
            await d.FocusItem(name);
            d.Press(InputAction.ToggleSelection);
        }
        d.Press(InputAction.OpenContextMenu);
        var menu = await d.WaitMenu();
        Assert.StartsWith("Extrair cada um para a própria pasta (3)", menu.Items[menu.FocusIndex].Label, StringComparison.Ordinal); // foco inicial; notas.txt fica de fora
        d.Press(InputAction.Confirm);
        d.ChooseOption(await d.WaitDialog("Extrair 3 compactados"), "Extrair");

        var summary = await d.WaitDialog("Extração de 3 compactados concluída");
        Assert.Contains(summary.Lines, l => l.Label == "docs.zip" && l.Value.EndsWith("→ docs", StringComparison.Ordinal));
        Assert.Contains(summary.Lines, l => l.Label == "fotos.tar.gz" && l.Value.StartsWith("concluída", StringComparison.Ordinal));
        Assert.Contains(summary.Lines, l => l.Label == "fotos.zip" && l.Value.StartsWith("concluída", StringComparison.Ordinal));
        Assert.Equal(3, app.Operations.Items.Count(o => o.Kind == OperationKind.Extract && o.State == OperationState.Completed));

        Assert.Equal("docs", File.ReadAllText(_tmp.Sub("docs", "leia.txt")));
        var fotos = new[] { "fotos", "fotos (2)" }.Select(f => File.ReadAllText(_tmp.Sub(f, "leia.txt"))).Order(StringComparer.Ordinal);
        Assert.Equal(["fotos-tgz", "fotos-zip"], fotos);
        foreach (var folder in new[] { "docs", "fotos", "fotos (2)" })
            Assert.Single(Directory.EnumerateFileSystemEntries(_tmp.Sub(folder)));
        Assert.False(Directory.Exists(_tmp.Sub("notas")));
    });
}
