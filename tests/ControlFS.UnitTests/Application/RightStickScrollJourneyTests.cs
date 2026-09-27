using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Analógico direito (#175): rola só a superfície ativa, sem dar a volta, sem abrir nada e sem mexer no que está por baixo.</summary>
public class RightStickScrollJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Right_stick_scrolls_lists_grid_text_and_dialogs_only_where_they_are_active() => UiContext.Run(async () =>
    {
        for (var i = 0; i < 12; i++) File.WriteAllText(_tmp.Sub($"arquivo{i:00}.txt"), string.Join("\n", Enumerable.Range(1, 60).Select(n => $"linha {n} " + new string('x', 80))));
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        var list = app.Browser.List;
        var location = app.Browser.Location;

        // Lista: uma linha por passo; no topo não dá a volta; lados não sobem de pasta nem abrem nada.
        d.Press(InputAction.ScrollUp);
        Assert.Equal(0, list.FocusIndex);
        d.Press(InputAction.ScrollDown);
        d.Press(InputAction.ScrollDown);
        Assert.Equal(2, list.FocusIndex);
        d.Press(InputAction.ScrollLeft);
        d.Press(InputAction.ScrollRight);
        Assert.Equal(location, app.Browser.Location);
        Assert.Null(app.TopModal);
        for (var i = 0; i < 20; i++) d.Press(InputAction.ScrollDown);
        Assert.Equal(11, list.FocusIndex); // para no fim

        // Grade: uma linha (mesma coluna) por passo.
        await d.FocusItem("arquivo01.txt");
        app.SetGridLayout(columns: 4, rowsPerPage: 2);
        d.Press(InputAction.ChangeView);
        d.Press(InputAction.ScrollDown);
        Assert.Equal("arquivo05.txt", list.Focused?.Name);

        // Texto aberto: rola o texto (linhas e colunas); a lista por baixo não se mexe.
        d.Press(InputAction.Confirm);
        await d.Idle();
        var text = Assert.IsType<TextPreviewModal>(app.TopModal);
        app.ReportTextPreviewPage(20);
        d.Press(InputAction.ScrollDown);
        d.Press(InputAction.ScrollDown);
        d.Press(InputAction.ScrollRight);
        Assert.Equal((2, TextPreviewModal.ScrollColumns), (text.Top, text.Column));
        Assert.Equal("arquivo05.txt", list.Focused?.Name);
        d.Press(InputAction.Back);

        // Diálogo: a tela rola o corpo; a opção focada e a lista ficam onde estavam.
        var requested = new List<int>();
        app.ModalBodyScrollRequested += requested.Add;
        var dialog = app.ShowMessage("Detalhes", [.. Enumerable.Range(1, 40).Select(n => ($"Campo {n}", "valor"))]);
        var focus = dialog.FocusIndex;
        d.Press(InputAction.ScrollDown);
        d.Press(InputAction.ScrollUp);
        Assert.Equal([1, -1], requested);
        Assert.Equal(focus, dialog.FocusIndex);
        Assert.Same(dialog, app.TopModal);
        Assert.Equal("arquivo05.txt", list.Focused?.Name);
    });
}
