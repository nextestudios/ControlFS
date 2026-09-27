using ControlFS.Core.Models;

namespace ControlFS.Application.State;

public enum BreadcrumbKind
{
    /// <summary>Pasta do disco (ou a raiz de uma unidade).</summary>
    Folder,

    /// <summary>O próprio arquivo compactado: a fronteira entre o disco e o conteúdo do compactado.</summary>
    Archive,

    /// <summary>Pasta dentro do compactado.</summary>
    ArchiveFolder,

    /// <summary>Segmentos do meio recolhidos em "…"; ativar mostra a lista deles.</summary>
    Collapsed,

    /// <summary>
    /// Raiz da barra superior: "Meu computador" em caminhos do disco e de compactados, "Locais" nas demais telas. Leva ao
    /// início (no seletor de pasta, aos outros locais).
    /// </summary>
    Root,
}

/// <summary>
/// Um segmento do caminho. <see cref="ChildFocusId"/> é o item a focar ao chegar lá (a pasta de onde viemos).
/// </summary>
public sealed record Breadcrumb(string Label, BreadcrumbKind Kind, Location? Target, string? ChildFocusId, bool IsCurrent = false)
{
    public IReadOnlyList<Breadcrumb> Hidden { get; init; } = [];
}

/// <summary>Região da tela (navegador ou início) que recebe as ações de navegação.</summary>
public enum PaneRegion
{
    List,
    Breadcrumbs,
    /// <summary>Faixa de abas do navegador (com 2+ abas: Cima na barra superior entra; L1/R1 trocam de aba).</summary>
    Tabs,

    /// <summary>Acesso rápido da barra superior (Favoritos, Recentes, pastas do Windows, Meu computador, Lixeira).</summary>
    QuickAccess,
}

public static class BreadcrumbTrail
{
    /// <summary>Acima disto, os segmentos do meio viram "…".</summary>
    public const int MaxVisible = 6;

    /// <summary>
    /// Recolhe o meio de caminhos longos. Sempre visíveis: a raiz, a fronteira do compactado (e a pasta do disco que o
    /// contém) e os três últimos segmentos.
    /// </summary>
    public static IReadOnlyList<Breadcrumb> Collapse(IReadOnlyList<Breadcrumb> all)
    {
        if (all.Count <= MaxVisible) return all;
        var keep = new HashSet<int> { 0, all.Count - 3, all.Count - 2, all.Count - 1 };
        for (var i = 0; i < all.Count; i++)
            if (all[i].Kind == BreadcrumbKind.Archive)
            {
                keep.Add(i);
                if (i > 0) keep.Add(i - 1);
            }
        var visible = new List<Breadcrumb>();
        var hidden = new List<Breadcrumb>();
        for (var i = 0; i < all.Count; i++)
        {
            if (!keep.Contains(i))
            {
                hidden.Add(all[i]);
                continue;
            }
            if (hidden.Count > 0)
            {
                visible.Add(new Breadcrumb("…", BreadcrumbKind.Collapsed, null, null) { Hidden = hidden });
                hidden = [];
            }
            visible.Add(all[i]);
        }
        return visible;
    }
}
