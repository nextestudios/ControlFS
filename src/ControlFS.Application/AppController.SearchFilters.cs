using ControlFS.Application.State;
using ControlFS.Core.Actions;

namespace ControlFS.Application;

/// <summary>
/// Filtros da busca (#47): Norte nos resultados abre os filtros; Sul liga/desliga um tipo ou abre o seletor do tamanho/data, e o
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
                Detail: on ? "Filtrando por este tipo" : null, Icon: ActionIcon.Filter, Section: "Tipo"));
        }
        MenuModal? filters = null;
        // Tamanho e data têm várias faixas escondidas: abrem o seletor (#261). O menu de filtros fica por baixo e, ao escolher, é
        // substituído por outro com o valor novo (Voltar no seletor volta a ele sem mudar nada).
        var sizeIndex = items.Count;
        items.Add(ChoiceRow("Tamanho", filter.Size, SearchSizeChoices, size =>
        {
            if (filters is not null) CloseModal(filters);
            ApplySearchFilter(pane, search, filter with { Size = size }, sizeIndex);
        }, ActionIcon.Sort, section: "Tamanho e data", detail: "Faixa de tamanho dos resultados.", context: "Filtros"));
        var dateIndex = items.Count;
        items.Add(ChoiceRow("Modificado", filter.Date, SearchDateChoices, date =>
        {
            if (filters is not null) CloseModal(filters);
            ApplySearchFilter(pane, search, filter with { Date = date }, dateIndex);
        }, ActionIcon.Recent, section: "Tamanho e data", detail: "Período da última modificação.", context: "Filtros"));
        var clearIndex = items.Count;
        items.Add(new MenuItem("Limpar filtros", () => ApplySearchFilter(pane, search, SearchFilter.None, clearIndex),
            filter.IsActive ? null : "Nenhum filtro ativo.", Icon: ActionIcon.ClearFilter, Section: "Busca"));
        items.Add(new MenuItem("Outras ações da busca…", () => ShowSearchMenu(pane, search), Detail: "Mostrar na pasta, nova busca, subpastas, propriedades.", Icon: ActionIcon.Search, Section: "Busca"));
        var visible = search.VisibleResults().Count();
        filters = new MenuModal($"Filtros ({visible} de {search.ResultCount})", items) { Icon = ActionIcon.Filter, FocusIndex = Math.Clamp(focus, 0, items.Count - 1) };
        PushModal(filters);
    }

    /// <summary>Guarda o filtro na sessão e refiltra os resultados guardados (a busca em andamento continua).</summary>
    private void ApplySearchFilter(PaneState pane, SearchState search, SearchFilter filter, int reopenAt)
    {
        SearchFilter = filter;
        search.Filter = filter;
        if (pane.ActiveSearch == search) pane.List.SetItems(search.VisibleResults(), pane.List.FocusedId);
        ShowSearchFilters(pane, search, reopenAt);
    }

    private static IReadOnlyList<Choice<SearchSizeFilter>> SearchSizeChoices { get; } =
        [.. Enum.GetValues<SearchSizeFilter>().Select(v => new Choice<SearchSizeFilter>(v, SearchFilter.SizeName(v)))];

    private static IReadOnlyList<Choice<SearchDateFilter>> SearchDateChoices { get; } =
        [.. Enum.GetValues<SearchDateFilter>().Select(v => new Choice<SearchDateFilter>(v, SearchFilter.DateName(v)))];
}
