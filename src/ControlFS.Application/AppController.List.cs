using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>Estado da caixa de marcação do cabeçalho da lista.</summary>
public enum MarkAllState
{
    None,
    Some,
    All,
}

/// <summary>
/// Cabeçalho das colunas da lista (redesenho, fase C): a ordenação atual (null onde a ordem é fixa, como no início) e a
/// caixa de marcação (só onde marcar funciona). A view só desenha; a ordem continua a mesma do Menu → Ordenar por.
/// </summary>
public sealed record ListHeader(SortOrder? Sort, bool CanMark, MarkAllState Marks);

public sealed partial class AppController
{
    /// <summary>Cabeçalho da lista da tela atual.</summary>
    public ListHeader ListHeader
    {
        get
        {
            if (Screen == Screen.Home) return new ListHeader(null, false, MarkAllState.None);
            var pane = ActivePane;
            var list = pane.List;
            var canMark = CanMarkIn(pane) && list.SelectableCount > 0;
            var marks = list.SelectionCount == 0 ? MarkAllState.None : list.SelectionCount >= list.SelectableCount ? MarkAllState.All : MarkAllState.Some;
            return new ListHeader(list.Sort, canMark, canMark ? marks : MarkAllState.None);
        }
    }

    /// <summary>Onde X (Marcar) funciona: navegador, fora da busca, com a pasta carregada.</summary>
    private static bool CanMarkIn(PaneState pane) => pane is { Mode: PaneMode.Browse, IsLoading: false } && pane.Location is not SearchLocation;

    /// <summary>
    /// Mouse/toque num título de coluna: a mesma ordenação do Menu → Ordenar por/Ordem. Mesma coluna inverte a ordem;
    /// outra coluna ordena por ela em ordem crescente. O foco continua no mesmo item.
    /// </summary>
    public void PointerSortBy(SortField field)
    {
        if (TopModal is not null || Screen == Screen.Home) return;
        var list = ActivePane.List;
        var sort = list.Sort.Field == field ? list.Sort with { Descending = !list.Sort.Descending } : new SortOrder(field);
        list.SetSort(sort);
        StatusMessage = $"Ordenado por {SortLabel(sort.Field)} ({(sort.Descending ? "decrescente" : "crescente")}).";
        RaiseChanged();
    }

    /// <summary>Mouse/toque na caixa do cabeçalho: marca todos (como Ações → Marcar todos) ou limpa a marcação.</summary>
    public void PointerToggleMarkAll()
    {
        if (TopModal is not null || ListHeader is not { CanMark: true } header) return;
        var list = ActivePane.List;
        if (header.Marks == MarkAllState.All) list.ClearSelection();
        else
        {
            list.SelectAll();
            StatusMessage = $"{list.SelectionCount} item(ns) marcado(s).";
        }
        RaiseChanged();
    }

    /// <summary>
    /// Tipo mostrado na coluna "Tipo" e no painel de detalhes: pastas do Windows (Downloads, Documentos…, também quando
    /// abertas pelo disco) são "Pasta do sistema"; favoritos, "Pasta favorita".
    /// </summary>
    public string TypeNameOf(FileEntry entry)
    {
        if (IsFavoriteEntry(entry)) return "Pasta favorita";
        if (entry.Kind is EntryKind.KnownFolder or EntryKind.Directory && entry.FullPath is { } path && IsSystemFolderPath(path)) return "Pasta do sistema";
        return EntryText.TypeName(entry);
    }

    private bool IsSystemFolderPath(string path)
    {
        if (!ReferenceEquals(_systemFoldersSource, Places))
        {
            _systemFoldersSource = Places;
            _systemFolders = new HashSet<string>(Places.Where(p => p.Kind == EntryKind.KnownFolder && !IsFavoriteEntry(p) && p.FullPath is not null).Select(p => Path.TrimEndingDirectorySeparator(p.FullPath!)), StringComparer.OrdinalIgnoreCase);
        }
        return _systemFolders.Contains(Path.TrimEndingDirectorySeparator(path));
    }

    private IReadOnlyList<FileEntry>? _systemFoldersSource;
    private HashSet<string> _systemFolders = [];
}
