namespace MiniLocalChat.Infrastructure;

/// <summary>
/// Keeps the most recently used values up to a total size (e.g. bytes of decoded pictures); the least
/// recently used are dropped first. UI thread only.
/// </summary>
public sealed class LruCache<TKey, TValue>(long capacity, Func<TValue, long> sizeOf, IEqualityComparer<TKey>? comparer = null)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value, long Size)>> _nodes = new(comparer);
    private readonly LinkedList<(TKey Key, TValue Value, long Size)> _order = new();
    private long _size;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_nodes.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        Remove(key);
        var size = sizeOf(value);
        _nodes[key] = _order.AddFirst((key, value, size));
        _size += size;

        // The newest one always stays, even if it's bigger than the whole budget.
        while (_size > capacity && _order.Last is { } oldest && oldest != _order.First)
        {
            Remove(oldest.Value.Key);
        }
    }

    public void Remove(TKey key)
    {
        if (_nodes.Remove(key, out var node))
        {
            _order.Remove(node);
            _size -= node.Value.Size;
        }
    }

    public void Clear()
    {
        _nodes.Clear();
        _order.Clear();
        _size = 0;
    }
}
