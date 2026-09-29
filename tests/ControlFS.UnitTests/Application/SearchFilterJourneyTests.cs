using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Filtros da busca (#47): combinam tipos, refiltram sem refazer a busca e valem para a sessão.</summary>
public class SearchFilterJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static async Task Search(Driver d, string query)
    {
        d.Press(InputAction.Search);
        var kb = await d.WaitKeyboard();
        d.App.TypeSelectAll();
        d.TypeOnKeyboard(kb, query);
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => d.App.TopModal is null, "teclado fechado");
        await d.Idle();
    }

    private static string[] Shown(AppController app) => [.. app.Browser.List.Items.Select(i => i.Name).Order(StringComparer.Ordinal)];

    [Fact]
    public void Type_filters_combine_apply_to_the_same_results_and_persist_for_the_next_search() => UiContext.Run(async () =>
    {
        foreach (var name in new[] { "casa.jpg", "casa.mp4", "casa.pdf", "casa.zip" }) File.WriteAllText(_tmp.Sub(name), "x");
        _tmp.MakeDir("casa-pasta");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        await Search(d, "casa");
        Assert.Equal(5, app.Browser.List.Items.Count);
        var search = app.Browser.Search;

        // Norte abre os filtros; Sul liga "Imagens" e o menu continua aberto no mesmo item
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Imagens");
        var menu = await d.WaitMenu();
        Assert.StartsWith("Imagens ✓", menu.Items[menu.FocusIndex].Label);
        Assert.Equal(["casa.jpg"], Shown(app));

        // Combinável: "Vídeos" soma ao filtro de imagens; a busca não foi refeita
        await d.ChooseMenu("Vídeos");
        Assert.Equal(["casa.jpg", "casa.mp4"], Shown(app));
        Assert.Same(search, app.Browser.Search);
        Assert.StartsWith("Filtros (2 de 5)", (await d.WaitMenu()).Title);
        d.Press(InputAction.Back);
        Assert.Contains("filtros: Imagens, Vídeos", search!.Summary);

        // Vale para a sessão: a próxima busca já chega filtrada
        await Search(d, "cas");
        Assert.NotSame(search, app.Browser.Search);
        Assert.Equal(["casa.jpg", "casa.mp4"], Shown(app));

        // Limpar filtros mostra tudo de novo
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Limpar filtros");
        Assert.Equal(5, app.Browser.List.Items.Count);
    });

    [Fact]
    public void Size_and_date_filters_open_a_picker_and_cancelling_it_keeps_the_filters_menu_as_it_was() => UiContext.Run(async () =>
    {
        foreach (var name in new[] { "casa.jpg", "casa.pdf" }) File.WriteAllText(_tmp.Sub(name), "x");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        await Search(d, "casa");

        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Tamanho");
        var picker = await d.WaitMenu();
        Assert.True(picker.IsPicker);
        Assert.Equal(["qualquer", "menos de 1 MB", "1 MB a 100 MB", "100 MB a 1 GB", "mais de 1 GB"], picker.Items.Select(i => i.Label));
        d.Press(InputAction.Back); // cancelar: os filtros continuam como estavam, o menu de filtros volta
        var filters = await d.WaitMenu();
        Assert.StartsWith("Filtros", filters.Title, StringComparison.Ordinal);
        Assert.Equal("Tamanho: qualquer", filters.Items[filters.FocusIndex].Label);
        Assert.Equal(2, app.Browser.List.Items.Count);

        d.Press(InputAction.Confirm);
        var again = await d.WaitMenu();
        d.FocusMenu(again, again.Items.ToList().FindIndex(i => i.Label == "1 MB a 100 MB"));
        d.Press(InputAction.Confirm);
        var reopened = await d.WaitMenu();
        Assert.Equal("Tamanho: 1 MB a 100 MB", reopened.Items[reopened.FocusIndex].Label); // menu novo, no mesmo item, com o valor
        Assert.Empty(app.Browser.List.Items); // os dois arquivos são pequenos
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal); // só um menu de filtros ficou aberto
    });
}
