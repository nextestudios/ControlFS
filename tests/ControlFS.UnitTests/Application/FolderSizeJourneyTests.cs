using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class FolderSizeJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static string Line(DialogModal dialog, string label) => dialog.Lines.Single(l => l.Label == label).Value;

    [Fact]
    public void Properties_calculates_folder_size_off_the_ui_thread_and_back_cancels_keeping_the_partial() => UiContext.Run(async () =>
    {
        _tmp.MakeDir("Jogos", "salvos");
        File.WriteAllBytes(_tmp.Sub("Jogos", "a.bin"), new byte[1000]);
        File.WriteAllBytes(_tmp.Sub("Jogos", "salvos", "b.bin"), new byte[234]);
        var fs = new TestFileSystem(_tmp.Path);
        var app = new AppController(fs, new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("Jogos");

        // Ações → Propriedades → Calcular tamanho
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Propriedades");
        var dialog = await d.WaitDialog("Propriedades");
        d.ChooseOption(dialog, "Calcular tamanho");
        await d.Idle();
        Assert.Contains($"({1234:N0} bytes)", Line(dialog, "Tamanho"));
        Assert.Equal("2 arquivo(s), 1 pasta(s)", Line(dialog, "Conteúdo"));

        // Recalcular preso no meio: Voltar (Leste/B) cancela e mantém o parcial, sem fechar o diálogo
        fs.HoldMeasureUntilCancelled = true;
        d.ChooseOption(dialog, "Recalcular");
        await UiContext.WaitUntil(() => Line(dialog, "Tamanho").StartsWith("Calculando…", StringComparison.Ordinal), "parcial");
        Assert.Contains(app.Hints, h => h is { Action: InputAction.Back, Label: "Cancelar cálculo" });
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Same(dialog, app.TopModal);
        Assert.StartsWith("Parcial:", Line(dialog, "Tamanho"));
        Assert.Contains("cancelado", dialog.Message);
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
    });
}
