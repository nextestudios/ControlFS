using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Busca por nome (#46) em uma árvore real temporária, dirigida só por ações semânticas.</summary>
public class SearchJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static string? Label(AppController app, InputAction action) => app.Hints.SingleOrDefault(h => h.Action == action)?.Label;

    private async Task<(Driver Driver, TestFileSystem Fs)> OpenTempFolder()
    {
        var fs = new TestFileSystem(_tmp.Path);
        var app = new AppController(fs, new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        return (d, fs);
    }

    private static async Task Search(Driver d, string query)
    {
        d.Press(InputAction.Search);
        var kb = await d.WaitKeyboard();
        d.TypeOnKeyboard(kb, query);
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => d.App.TopModal is null, "teclado fechado");
    }

    [Fact]
    public void Search_streams_results_in_subfolders_and_opening_one_focuses_it_in_its_folder() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("relatorio.md"), "raiz");
        _tmp.MakeDir("a", "b");
        File.WriteAllText(_tmp.Sub("a", "b", "Relatório anual.txt"), "fundo");
        File.WriteAllText(_tmp.Sub("a", "outro.txt"), "x");
        var (d, fs) = await OpenTempFolder();
        var app = d.App;
        await d.FocusItem("a");
        fs.PauseSearchAfter = 1; // o primeiro resultado aparece enquanto a busca ainda procura

        await Search(d, "relatorio");
        await UiContext.WaitUntil(() => app.Browser.List.Items.Count == 1, "primeiro resultado na lista");
        var search = app.Browser.ActiveSearch!;
        Assert.True(search.IsRunning);
        Assert.Contains("parcial", search.Summary, StringComparison.Ordinal);
        Assert.Equal("Cancelar busca", Label(app, InputAction.Back));

        fs.ResumeSearch();
        await d.Idle();
        Assert.Equal(SearchStatus.Completed, search.Status);
        Assert.Equal(["Relatório anual.txt", "relatorio.md"], app.Browser.List.Items.Select(i => i.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Voltar", Label(app, InputAction.Back));

        // Abrir um resultado leva à pasta real dele com o foco no item; Voltar retorna aos resultados.
        await d.FocusItem("Relatório anual.txt");
        Assert.Equal("Mostrar na pasta", Label(app, InputAction.Confirm));
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(_tmp.Sub("a", "b"), ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal("Relatório anual.txt", app.Browser.List.Focused?.Name);

        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Same(search, app.Browser.ActiveSearch);
        Assert.Equal("Relatório anual.txt", app.Browser.List.Focused?.Name);

        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal("a", app.Browser.List.Focused?.Name); // a pasta de origem volta com o foco de antes
    });

    [Fact]
    public void Back_cancels_a_running_search_keeping_partial_results_and_stops_enumerating() => UiContext.Run(async () =>
    {
        for (var i = 0; i < 5; i++) File.WriteAllText(Path.Join(_tmp.MakeDir($"p{i}"), $"alvo{i}.txt"), "x");
        var (d, fs) = await OpenTempFolder();
        var app = d.App;
        fs.PauseSearchAfter = 2;

        await Search(d, "alvo");
        await UiContext.WaitUntil(() => app.Browser.List.Items.Count == 2, "resultados parciais");
        var search = app.Browser.ActiveSearch!;

        d.Press(InputAction.Back); // East/B: cancela a busca, não sai dela
        Assert.Equal(SearchStatus.Cancelled, search.Status);
        Assert.Same(search, app.Browser.ActiveSearch);
        Assert.Equal("Voltar", Label(app, InputAction.Back));
        await d.Idle();
        Assert.True(fs.SearchEnded);
        Assert.Equal(SearchStatus.Cancelled, search.Status);
        Assert.Equal(2, app.Browser.List.Items.Count);
        Assert.Contains("cancelada", search.Summary, StringComparison.Ordinal);

        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
    });
}
