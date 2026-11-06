namespace D3skCommander.Core.Index;

/// <summary>
/// Collects entries into the arrays of a <see cref="NameIndex"/>: one string pool, then per entry a parent,
/// a size, a time and a flag. A parent is added before its children.
/// </summary>
public sealed class NameIndexBuilder
{
    private char[] _pool = new char[1 << 16];
    private int[] _start = new int[1 << 12];
    private int[] _parent = new int[1 << 12];
    private long[] _size = new long[1 << 12];
    private uint[] _time = new uint[1 << 12];
    private byte[] _flags = new byte[1 << 12];
    private int _poolLength;

    public int Count { get; private set; }

    /// <summary>Adds an entry and returns its id. A root has parent -1 and its full path as name.</summary>
    public int Add(ReadOnlySpan<char> name, int parent, long size, DateTimeOffset modified, bool isFolder)
    {
        if (parent < -1 || parent >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(parent), parent, "A parent is added before its children.");
        }

        if (Count == _parent.Length)
        {
            Grow();
        }

        EnsurePool(name.Length);
        var id = Count++;
        _start[id] = _poolLength;
        name.CopyTo(_pool.AsSpan(_poolLength));
        _poolLength += name.Length;
        _parent[id] = parent;
        _size[id] = Math.Max(0, size);
        _time[id] = NameIndex.StampOf(modified);
        _flags[id] = isFolder ? NameIndex.FolderFlag : (byte)0;
        return id;
    }

    public NameIndex Build(DateTimeOffset builtAt)
    {
        var start = new int[Count + 1];
        Array.Copy(_start, start, Count);
        start[Count] = _poolLength;
        return NameIndex.Create(
            _pool.AsSpan(0, _poolLength).ToArray(),
            start,
            _parent.AsSpan(0, Count).ToArray(),
            _size.AsSpan(0, Count).ToArray(),
            _time.AsSpan(0, Count).ToArray(),
            _flags.AsSpan(0, Count).ToArray(),
            builtAt) ?? throw new InvalidOperationException("The builder produced an invalid index.");
    }

    private void EnsurePool(int extra)
    {
        if (_poolLength + extra <= _pool.Length)
        {
            return;
        }

        var size = _pool.Length;
        while (size < _poolLength + extra)
        {
            size *= 2;
        }

        Array.Resize(ref _pool, size);
    }

    private void Grow()
    {
        var size = _parent.Length * 2;
        Array.Resize(ref _start, size);
        Array.Resize(ref _parent, size);
        Array.Resize(ref _size, size);
        Array.Resize(ref _time, size);
        Array.Resize(ref _flags, size);
    }
}
