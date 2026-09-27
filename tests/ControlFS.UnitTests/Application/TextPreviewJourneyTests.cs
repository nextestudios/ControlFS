using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class TextPreviewJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void South_opens_text_read_only_scrolls_with_the_controller_and_binary_is_refused_from_the_menu() => UiContext.Run(async () =>
    {
        var notes = string.Join("\r\n", Enumerable.Range(1, 100).Select(i => $"linha {i}\t" + new string('x', i)));
        File.WriteAllText(_tmp.Sub("notas.log"), notes);
        File.WriteAllBytes(_tmp.Sub("programa.dat"), [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("notas.log");
        d.Press(InputAction.Confirm);
        await d.Idle();

        var preview = Assert.IsType<TextPreviewModal>(app.TopModal);
        var document = preview.Document!;
        Assert.Equal(100, document.Lines.Count);
        Assert.Equal("linha 1 x", document.Lines[0]); // tabulação expandida até a coluna 8

        app.ReportTextPreviewPage(20);
        d.Press(InputAction.NavigateDown);
        Assert.Equal(1, preview.Top);
        d.Press(InputAction.PageDown); // RT: uma página menos uma linha de contexto
        Assert.Equal(20, preview.Top);
        d.Press(InputAction.NextRegion); // RB: fim, sem passar da última página
        Assert.Equal(80, preview.Top);
        d.Press(InputAction.NavigateRight);
        Assert.Equal(TextPreviewModal.ColumnStep, preview.Column);
        d.Press(InputAction.Confirm);
        Assert.False(preview.Monospace);
        Assert.Equal(notes, File.ReadAllText(_tmp.Sub("notas.log"))); // somente leitura
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
        Assert.Equal("notas.log", app.Browser.List.Focused?.Name);

        await d.FocusItem("programa.dat");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Visualizar como texto");
        await d.Idle();
        var refused = Assert.IsType<TextPreviewModal>(app.TopModal);
        Assert.Null(refused.Document);
        Assert.Contains("binário", refused.Error, StringComparison.Ordinal);
    });
}
