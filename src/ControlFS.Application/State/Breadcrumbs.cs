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
}

/// <summary>
/// Um segmento do caminho. <see cref="ChildFocusId"/> é o item a focar ao chegar lá (a pasta de onde viemos).
/// </summary>
public sealed record Breadcrumb(string Label, BreadcrumbKind Kind, Location? Target, string? ChildFocusId, bool IsCurrent = false)
{
    public IReadOnlyList<Breadcrumb> Hidden { get; init; } = [];
}

/// <summary>Região da tela do navegador que recebe as ações de navegação.</summary>
public enum PaneRegion
{
    List,
    Breadcrumbs,
    /// <summary>Faixa de abas do navegador (RB entra; LB/RB trocam de aba).</summary>
    Tabs,
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
