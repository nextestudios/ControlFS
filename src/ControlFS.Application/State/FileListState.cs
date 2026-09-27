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

    /// <summary>
    /// Substitui os itens preservando o foco pelo Id. Se o item focado sumiu (excluído, movido, renomeado fora do app),
    /// o foco vai para o próximo item que sobreviveu na ordem anterior; sem próximo, para o anterior. Com itens na lista,
    /// o foco nunca fica vazio.
    /// </summary>
    public void SetItems(IEnumerable<FileEntry> items, string? preferFocusId = null, bool keepSelection = false)
    {
        var previous = _items;
        var previousIndex = FocusIndex;
        var targetId = preferFocusId ?? FocusedId;
        _items = items.OrderBy(i => i, Sort.CreateComparer()).ToList();
        if (keepSelection) _selected.IntersectWith(_items.Select(i => i.Id));
        else _selected.Clear();
        FocusIndex = FindIndex(targetId) ?? FindSurvivingNeighbor(previous, previousIndex)
            ?? (_items.Count == 0 ? -1 : Math.Clamp(previousIndex, 0, _items.Count - 1));
    }

    /// <summary>
    /// Acrescenta itens que chegam aos poucos (busca em andamento) na ordem atual, sem reordenar a lista inteira. O foco
    /// continua no mesmo item e a marcação é mantida; lista que estava vazia recebe o foco no primeiro item.
    /// </summary>
    public void AppendItems(IReadOnlyCollection<FileEntry> items)
    {
        if (items.Count == 0) return;
        var focusedId = FocusedId;
        var comparer = Sort.CreateComparer();
        var incoming = items.OrderBy(i => i, comparer).ToList();
        var merged = new List<FileEntry>(_items.Count + incoming.Count);
        int a = 0, b = 0;
        while (a < _items.Count && b < incoming.Count) merged.Add(comparer.Compare(_items[a], incoming[b]) <= 0 ? _items[a++] : incoming[b++]);
        for (; a < _items.Count; a++) merged.Add(_items[a]);
        for (; b < incoming.Count; b++) merged.Add(incoming[b]);
        _items = merged;
        FocusIndex = FindIndex(focusedId) ?? 0;
    }

    private int? FindSurvivingNeighbor(List<FileEntry> previous, int previousIndex)
    {
        if (previousIndex < 0 || previousIndex >= previous.Count || _items.Count == 0) return null;
        var positions = new Dictionary<string, int>(_items.Count, StringComparer.Ordinal);
        for (var i = 0; i < _items.Count; i++) positions.TryAdd(_items[i].Id, i);
        for (var i = previousIndex + 1; i < previous.Count; i++)
            if (positions.TryGetValue(previous[i].Id, out var next)) return next;
        for (var i = previousIndex - 1; i >= 0; i--)
            if (positions.TryGetValue(previous[i].Id, out var before)) return before;
        return null;
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
