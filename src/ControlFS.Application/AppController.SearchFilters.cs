using ControlFS.Application.State;

namespace ControlFS.Application;

/// <summary>
/// Filtros da busca (#47): Norte nos resultados abre os filtros; Sul liga/desliga um tipo ou avança o tamanho/data, e o
/// menu continua aberto no mesmo item. Os filtros valem para a sessão (também nas próximas buscas) e são aplicados aos
/// resultados já encontrados, sem refazer a busca.
/// </summary>
public sealed partial class AppController
{
    /// <summary>Filtros da sessão: aplicados a toda busca nova até serem limpos.</summary>
    public SearchFilter SearchFilter { get; private set; } = SearchFilter.None;

    private void ShowSearchFilters(PaneState pane, SearchState search, int focus = 0)
    {
        var filter = SearchFilter;
        var items = new List<MenuItem>();
        foreach (var type in SearchFilter.AllTypes)
        {
            var on = (filter.Types & type) != 0;
            var index = items.Count;
            items.Add(new MenuItem(SearchFilter.TypeName(type) + (on ? " ✓" : string.Empty),
                () => ApplySearchFilter(pane, search, filter with { Types = filter.Types ^ type }, index),
                Detail: on ? "Filtrando por este tipo" : null));
        }
        var sizeIndex = items.Count;
        items.Add(new MenuItem($"Tamanho: {SearchFilter.SizeName(filter.Size)}",
            () => ApplySearchFilter(pane, search, filter with { Size = Next(filter.Size) }, sizeIndex), Detail: "Sul troca a faixa."));
        var dateIndex = items.Count;
        items.Add(new MenuItem($"Modificado: {SearchFilter.DateName(filter.Date)}",
            () => ApplySearchFilter(pane, search, filter with { Date = Next(filter.Date) }, dateIndex), Detail: "Sul troca o período."));
        var clearIndex = items.Count;
        items.Add(new MenuItem("Limpar filtros", () => ApplySearchFilter(pane, search, SearchFilter.None, clearIndex),
            filter.IsActive ? null : "Nenhum filtro ativo."));
        items.Add(new MenuItem("Outras ações da busca…", () => ShowSearchMenu(pane, search), Detail: "Mostrar na pasta, nova busca, subpastas, propriedades."));
        var visible = search.VisibleResults().Count();
        PushModal(new MenuModal($"Filtros ({visible} de {search.ResultCount})", items) { FocusIndex = Math.Clamp(focus, 0, items.Count - 1) });
    }

    /// <summary>Guarda o filtro na sessão e refiltra os resultados guardados (a busca em andamento continua).</summary>
    private void ApplySearchFilter(PaneState pane, SearchState search, SearchFilter filter, int reopenAt)
    {
        SearchFilter = filter;
        search.Filter = filter;
        if (pane.ActiveSearch == search) pane.List.SetItems(search.VisibleResults(), pane.List.FocusedId);
        ShowSearchFilters(pane, search, reopenAt);
    }

    private static T Next<T>(T value) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return values[(Array.IndexOf(values, value) + 1) % values.Length];
    }
}
