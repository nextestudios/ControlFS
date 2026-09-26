using ControlFS.Core.Models;

namespace ControlFS.Application.State;

/// <summary>
/// Lista navegável. Foco, seleção e rolagem são estados distintos; o foco é mantido pela
/// identidade do item (Id), não pelo elemento visual, e sobrevive a reordenação e atualização.
/// </summary>
public sealed class FileListState
{
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private List<FileEntry> _items = [];

    public IReadOnlyList<FileEntry> Items => _items;
    public int FocusIndex { get; private set; } = -1;
    public FileEntry? Focused => FocusIndex >= 0 && FocusIndex < _items.Count ? _items[FocusIndex] : null;
    public string? FocusedId => Focused?.Id;
    public IReadOnlyCollection<string> SelectedIds => _selected;
    public int SelectionCount => _selected.Count;
    public SortOrder Sort { get; private set; } = SortOrder.Default;

    public bool IsSelected(FileEntry entry) => _selected.Contains(entry.Id);

    public IReadOnlyList<FileEntry> SelectedEntries => _items.Where(i => _selected.Contains(i.Id)).ToList();

    /// <summary>Substitui os itens preservando o foco pelo Id; se o item sumiu, foca o vizinho no mesmo índice.</summary>
    public void SetItems(IEnumerable<FileEntry> items, string? preferFocusId = null, bool keepSelection = false)
    {
        var previousIndex = FocusIndex;
        var targetId = preferFocusId ?? FocusedId;
        _items = items.OrderBy(i => i, Sort.CreateComparer()).ToList();
        if (keepSelection) _selected.IntersectWith(_items.Select(i => i.Id));
        else _selected.Clear();
        FocusIndex = FindIndex(targetId) ?? (_items.Count == 0 ? -1 : Math.Clamp(previousIndex, 0, _items.Count - 1));
    }

    public void SetSort(SortOrder sort)
    {
        var id = FocusedId;
        Sort = sort;
        _items = _items.OrderBy(i => i, Sort.CreateComparer()).ToList();
        FocusIndex = FindIndex(id) ?? (_items.Count == 0 ? -1 : 0);
    }

    public bool Move(int delta)
    {
        if (_items.Count == 0) return false;
        var target = Math.Clamp(FocusIndex + delta, 0, _items.Count - 1);
        if (target == FocusIndex) return false;
        FocusIndex = target;
        return true;
    }

    public bool FocusById(string id)
    {
        if (FindIndex(id) is not int index) return false;
        FocusIndex = index;
        return true;
    }

    public bool ToggleFocusedSelection()
    {
        if (Focused is not { } entry || entry.Kind is EntryKind.Drive or EntryKind.KnownFolder) return false;
        if (!_selected.Remove(entry.Id)) _selected.Add(entry.Id);
        return true;
    }

    public void SelectAll()
    {
        foreach (var i in _items)
            if (i.Kind is not (EntryKind.Drive or EntryKind.KnownFolder) && !i.IsBlocked) _selected.Add(i.Id);
    }

    public void ClearSelection() => _selected.Clear();

    private int? FindIndex(string? id)
    {
        if (id is null) return null;
        var index = _items.FindIndex(i => i.Id == id);
        return index >= 0 ? index : null;
    }
}
