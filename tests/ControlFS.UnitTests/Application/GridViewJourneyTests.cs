using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class GridViewJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    [Fact]
    public void Grid_moves_in_2D_without_skipping_wraps_at_row_ends_and_switching_views_keeps_the_focused_item() => UiContext.Run(async () =>
    {
        // 7 itens em 3 colunas: a última linha tem só um item.
        foreach (var name in new[] { "a", "b", "c", "d", "e", "f", "g" }) File.WriteAllText(_tmp.Sub(name + ".txt"), name);
        var store = new JsonSettingsStore(_data.Path);
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store);
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        await d.FocusItem("b.txt");

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Exibição: lista");
        Assert.True(app.IsGrid);
        Assert.Equal("b.txt", app.ActivePane.List.Focused!.Name); // trocar de visualização não perde o foco
        app.SetGridLayout(columns: 3, rowsPerPage: 2);
        string Focused() => app.ActivePane.List.Focused!.Name;

        // Direita percorre tudo, um a um, passando para a linha de baixo na ponta; no último item, para.
        d.Press(InputAction.NavigateLeft);
        var folder = app.ActivePane.Location;
        var visited = new List<string> { Focused() };
        for (var i = 0; i < 8; i++)
        {
            d.Press(InputAction.NavigateRight);
            if (Focused() != visited[^1]) visited.Add(Focused());
        }
        Assert.Equal(["a.txt", "b.txt", "c.txt", "d.txt", "e.txt", "f.txt", "g.txt"], visited);

        // Esquerda no início da linha volta para o fim da linha de cima (e nunca sobe de pasta).
        d.Press(InputAction.NavigateUp); // g → d
        Assert.Equal("d.txt", Focused());
        d.Press(InputAction.NavigateLeft);
        Assert.Equal("c.txt", Focused());
        Assert.Equal(folder, app.ActivePane.Location);

        // Baixo mantém a coluna; para uma última linha incompleta, vai ao último item. Na borda, fica.
        d.Press(InputAction.NavigateDown); // c → f
        Assert.Equal("f.txt", Focused());
        d.Press(InputAction.NavigateDown); // f → g (linha incompleta)
        Assert.Equal("g.txt", Focused());
        d.Press(InputAction.NavigateDown);
        Assert.Equal("g.txt", Focused());
        d.Press(InputAction.PageUp); // duas linhas acima, mesma coluna
        Assert.Equal("a.txt", Focused());

        // Volta para a lista: mesmo item focado, preferência salva.
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.ChangeView);
        Assert.False(app.IsGrid);
        Assert.Equal("b.txt", Focused());
        Assert.Equal(ViewMode.List, store.Load().Settings.View);
    });
}
