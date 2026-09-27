using System.Diagnostics.CodeAnalysis;

namespace ControlFS.Core.Icons;

/// <summary>Cache limitado: ao passar da capacidade, descarta o item usado há mais tempo. Não é thread-safe.</summary>
public sealed class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public int Count => _map.Count;

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (_map.Remove(key, out var existing)) _order.Remove(existing);
        _map[key] = _order.AddFirst((key, value));
        while (_map.Count > Capacity)
        {
            var last = _order.Last!;
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
        }
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }
}
