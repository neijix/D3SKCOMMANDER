using System.Text;

namespace D3skCommander.Core.Index;

/// <summary>
/// Every file and folder name of some roots, in a few flat arrays and no object per entry: one pool of all the
/// names, then per entry the start of its name, its parent, size, time (seconds since 1970) and a flag. A path is
/// rebuilt from the parents, never stored. A parent always has a lower id than its children.
/// </summary>
public sealed partial class NameIndex
{
    internal const byte FolderFlag = 1;

    private readonly char[] _pool;
    private readonly int[] _start;
    private readonly int[] _parent;
    private readonly long[] _size;
    private readonly uint[] _time;
    private readonly byte[] _flags;
    private readonly int[] _pathLength;

    private NameIndex(char[] pool, int[] start, int[] parent, long[] size, uint[] time, byte[] flags, DateTimeOffset builtAt)
    {
        _pool = pool;
        _start = start;
        _parent = parent;
        _size = size;
        _time = time;
        _flags = flags;
        BuiltAt = builtAt;
        _pathLength = new int[parent.Length];
        for (var i = 0; i < parent.Length; i++)
        {
            var own = start[i + 1] - start[i];
            _pathLength[i] = parent[i] < 0 ? own : _pathLength[parent[i]] + 1 + own;
        }
    }

    public static NameIndex Empty { get; } = new([], [0], [], [], [], [], DateTimeOffset.UnixEpoch);

    public int Count => _parent.Length;

    /// <summary>When the walk that produced this index finished.</summary>
    public DateTimeOffset BuiltAt { get; }

    internal ReadOnlySpan<char> Pool => _pool;

    internal ReadOnlySpan<int> Starts => _start;

    internal ReadOnlySpan<int> Parents => _parent;

    internal ReadOnlySpan<long> Sizes => _size;

    internal ReadOnlySpan<uint> Times => _time;

    internal ReadOnlySpan<byte> Flags => _flags;

    public bool IsFolder(int id) => (_flags[id] & FolderFlag) != 0;

    public string NameOf(int id) => new(NameAt(id));

    public long SizeOf(int id) => _size[id];

    public DateTimeOffset? ModifiedOf(int id) => _time[id] == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(_time[id]);

    /// <summary>The full path of an entry, joined from the names of its parents.</summary>
    public string PathOf(int id)
    {
        CheckId(id);
        var depth = 0;
        for (var at = id; at >= 0; at = _parent[at])
        {
            depth++;
        }

        var chain = new int[depth];
        for (var at = id; at >= 0; at = _parent[at])
        {
            chain[--depth] = at;
        }

        var path = new StringBuilder(_pathLength[id] + 1);
        foreach (var link in chain)
        {
            if (path.Length > 0 && path[^1] != Path.DirectorySeparatorChar && path[^1] != Path.AltDirectorySeparatorChar)
            {
                path.Append(Path.DirectorySeparatorChar);
            }

            path.Append(NameAt(link));
        }

        return path.ToString();
    }

    /// <summary>The folder that holds an entry; for a root, the folder above it ("" when there is none).</summary>
    public string FolderOf(int id)
    {
        CheckId(id);
        return _parent[id] >= 0 ? PathOf(_parent[id]) : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(NameOf(id))) ?? "";
    }

    /// <summary>Seconds since 1970 for the time array; before 1970 is 0, which reads as unknown.</summary>
    internal static uint StampOf(DateTimeOffset time)
    {
        var seconds = time.ToUnixTimeSeconds();
        return seconds <= 0 ? 0 : seconds >= uint.MaxValue ? uint.MaxValue : (uint)seconds;
    }

    /// <summary>The index over these arrays, or null when they are not a consistent index (a corrupt file).</summary>
    internal static NameIndex? Create(char[] pool, int[] start, int[] parent, long[] size, uint[] time, byte[] flags, DateTimeOffset builtAt)
    {
        var count = parent.Length;
        if (start.Length != count + 1 || size.Length != count || time.Length != count || flags.Length != count
            || start[0] != 0 || start[count] != pool.Length)
        {
            return null;
        }

        for (var i = 0; i < count; i++)
        {
            if (start[i + 1] < start[i] || parent[i] < -1 || parent[i] >= i || (parent[i] >= 0 && (flags[parent[i]] & FolderFlag) == 0))
            {
                return null;
            }
        }

        return new NameIndex(pool, start, parent, size, time, flags, builtAt);
    }

    private ReadOnlySpan<char> NameAt(int id) => _pool.AsSpan(_start[id], _start[id + 1] - _start[id]);

    private void CheckId(int id)
    {
        if ((uint)id >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "No such entry.");
        }
    }
}
