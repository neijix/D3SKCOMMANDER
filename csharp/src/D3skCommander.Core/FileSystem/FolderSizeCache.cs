namespace D3skCommander.Core.FileSystem;

/// <summary>
/// Measured folder sizes for the session, keyed by folder path and the folder's last-write time:
/// a folder whose time changed is stale and misses. Thread-safe; past <see cref="Capacity"/> the oldest entries go.
/// </summary>
public sealed class FolderSizeCache
{
    public const int DefaultCapacity = 50_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime LastWrite, ulong Bytes)> _items = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public FolderSizeCache(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>The cached bytes when <paramref name="path"/> was stored with this same <paramref name="lastWrite"/>, else null.</summary>
    public ulong? Get(string path, DateTime lastWrite)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            if (_items.TryGetValue(path, out var item) && item.LastWrite == lastWrite)
            {
                return item.Bytes;
            }

            return null;
        }
    }

    public void Set(string path, DateTime lastWrite, ulong bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            if (!_items.ContainsKey(path))
            {
                _order.Enqueue(path);
            }

            _items[path] = (lastWrite, bytes);
            while (_items.Count > Capacity && _order.TryDequeue(out var oldest))
            {
                _items.Remove(oldest);
            }
        }
    }
}
