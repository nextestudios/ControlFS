using ControlFS.Core.Models;

namespace ControlFS.Application.Operations;

/// <summary>
/// Histórico limitado de operações concluídas (#20), da mais antiga para a mais recente. As mais antigas saem quando o
/// limite é atingido. Deve ser usado na thread de UI.
/// </summary>
public sealed class OperationHistory
{
    public const int MaxEntries = 200;
    private readonly List<OperationHistoryEntry> _entries = [];

    public IReadOnlyList<OperationHistoryEntry> Entries => _entries;

    public OperationHistoryEntry? Find(string? id) => id is null ? null : _entries.Find(e => e.Id == id);

    internal void Load(IEnumerable<OperationHistoryEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries.OrderBy(e => e.FinishedAt));
        Trim();
    }

    internal void Add(OperationHistoryEntry entry)
    {
        _entries.Add(entry);
        Trim();
    }

    internal void Clear() => _entries.Clear();

    private void Trim()
    {
        if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
    }
}
