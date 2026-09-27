using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Uso do disco (#72): ranking com totais reais, descer e subir sem reler o disco, abrir um arquivo e cancelar.</summary>
public class DiskUsageJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Ranks_folders_and_files_by_size_drills_down_and_back_opens_a_file_and_cancels_promptly() => UiContext.Run(async () =>
    {
        _tmp.MakeDir("Jogos", "Grande", "saves");
        _tmp.MakeDir("Jogos", "Pequeno");
        File.WriteAllBytes(_tmp.Sub("Jogos", "Grande", "dados.pak"), new byte[30_000]);
        File.WriteAllBytes(_tmp.Sub("Jogos", "Grande", "saves", "s1.sav"), new byte[2_000]);
        File.WriteAllBytes(_tmp.Sub("Jogos", "Pequeno", "p.bin"), new byte[500]);
        File.WriteAllBytes(_tmp.Sub("Jogos", "leia.txt"), new byte[40_000]);
        var fs = new TestFileSystem(_tmp.Path);
        var app = new AppController(fs, new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("Jogos");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Analisar uso do disco");

        await UiContext.WaitUntil(() => app.TopModal is MenuModal { Title: "Uso do disco: Jogos" }, "resultado");
        var root = (MenuModal)app.TopModal!;
        Assert.StartsWith(AppController.FormatBytes(72_500), root.Subtitle, StringComparison.Ordinal);
        Assert.Contains("4 arquivos · 3 pastas", root.Subtitle, StringComparison.Ordinal);
        var labels = root.Items.Select(i => i.Label).ToList();
        Assert.True(labels.FindIndex(l => l.StartsWith("Grande —", StringComparison.Ordinal)) < labels.FindIndex(l => l.StartsWith("Pequeno —", StringComparison.Ordinal)));
        Assert.Contains(labels, l => l.StartsWith("leia.txt —", StringComparison.Ordinal));
        Assert.StartsWith("Grande —", root.Items[root.FocusIndex].Label, StringComparison.Ordinal); // a maior pasta já em foco

        d.Press(InputAction.Confirm); // desce em "Grande"
        await UiContext.WaitUntil(() => app.TopModal is MenuModal { Title: "Uso do disco: Grande" }, "nível de baixo");
        Assert.StartsWith(AppController.FormatBytes(32_000), ((MenuModal)app.TopModal!).Subtitle, StringComparison.Ordinal);
        d.Press(InputAction.Back);
        Assert.Same(root, app.TopModal); // Voltar sobe um nível

        await d.ChooseMenu("leia.txt");
        await d.Idle();
        Assert.Null(app.TopModal); // todos os níveis fecham
        Assert.Equal(_tmp.Sub("Jogos"), (app.Browser.Location as PhysicalLocation)?.FullPath);
        Assert.Equal("leia.txt", app.Browser.List.Focused?.Name);

        // Cancelar: Voltar interrompe a leitura na hora e nada fica aberto.
        fs.HoldMeasureUntilCancelled = true;
        await d.FocusItem("Grande");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Analisar uso do disco");
        var scanning = await d.WaitDialog("Analisando uso do disco");
        await UiContext.WaitUntil(() => scanning.Lines.Any(l => l.Value.Contains("arquivo", StringComparison.Ordinal)), "parcial");
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Null(app.TopModal);
        Assert.Contains("cancelada", app.StatusMessage, StringComparison.Ordinal);
    });
}
