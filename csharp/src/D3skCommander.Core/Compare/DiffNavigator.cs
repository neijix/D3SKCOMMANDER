namespace D3skCommander.Core.Compare;

/// <summary>Difference blocks, and the hex peek around a binary difference.</summary>
public static class DiffNavigator
{
    /// <summary>Rows of 16 bytes shown around the first differing byte.</summary>
    public const int PeekRows = 8;

    public const int RowBytes = 16;

    private const int PeekBytes = PeekRows * RowBytes;

    /// <summary>Number of difference blocks (runs of non-equal rows).</summary>
    public static int BlockCount(IReadOnlyList<DiffRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var count = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (TextCompare.IsBlockStart(rows, i))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The run of non-equal rows containing <paramref name="row"/>. An equal row is an empty span.</summary>
    public static BlockSpan BlockRange(IReadOnlyList<DiffRow> rows, int row)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (row < 0 || row >= rows.Count || rows[row].Kind == RowKind.Equal)
        {
            return new BlockSpan(row, row);
        }

        var start = row;
        var previous = row - 1;
        while (previous >= 0 && rows[previous].Kind != RowKind.Equal)
        {
            start = previous;
            previous--;
        }

        var end = row;
        var index = row;
        while (index < rows.Count && rows[index].Kind != RowKind.Equal)
        {
            index++;
            end = index;
        }

        return new BlockSpan(start, end);
    }

    /// <summary>First byte shown for a difference at <paramref name="first"/>: two rows of context before it.</summary>
    public static long PeekStart(long first)
    {
        if (first < 0)
        {
            return 0;
        }

        var row = first / RowBytes;
        if (row < 2)
        {
            return 0;
        }

        return (row - 2) * RowBytes;
    }

    /// <summary>Reads the bytes around <paramref name="first"/> from both files.</summary>
    public static BinaryPeek ReadPeek(string left, string right, long first)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var start = PeekStart(first);
        return new BinaryPeek(start, first, ReadAt(left, start), ReadAt(right, start));
    }

    private static byte[] ReadAt(string path, long start)
    {
        using var stream = File.OpenRead(path);
        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[PeekBytes];
        var count = ByteRead.Full(stream, buffer);
        if (count == buffer.Length)
        {
            return buffer;
        }

        var sized = new byte[count];
        Buffer.BlockCopy(buffer, 0, sized, 0, count);
        return sized;
    }
}
