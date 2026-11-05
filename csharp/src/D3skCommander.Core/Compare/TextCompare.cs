using DiffPlex;
using DiffPlex.Model;

namespace D3skCommander.Core.Compare;

/// <summary>Side-by-side text diff and the next/previous difference block.</summary>
public static class TextCompare
{
    /// <summary>
    /// A deleted run followed by an inserted run pairs up line by line as <see cref="RowKind.Changed"/>;
    /// the surplus lines are <see cref="RowKind.LeftOnly"/> or <see cref="RowKind.RightOnly"/>.
    /// </summary>
    public static IReadOnlyList<DiffRow> DiffText(string left, string right, DiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var leftLines = TextLines.Split(left);
        var rightLines = TextLines.Split(right);
        var diff = Differ.Instance.CreateDiffs(left, right, false, false, new NormalizedChunker(options));
        return RowsFrom(leftLines, rightLines, diff.DiffBlocks);
    }

    /// <summary>True when row <paramref name="index"/> begins a run of non-equal rows.</summary>
    public static bool IsBlockStart(IReadOnlyList<DiffRow> rows, int index)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows[index].Kind != RowKind.Equal && (index == 0 || rows[index - 1].Kind == RowKind.Equal);
    }

    /// <summary>First row after <paramref name="from"/> that starts a difference block.</summary>
    public static int? NextDifference(IReadOnlyList<DiffRow> rows, int from)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (from == int.MaxValue)
        {
            return null;
        }

        for (var i = from + 1; i < rows.Count; i++)
        {
            if (IsBlockStart(rows, i))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Start of the nearest difference block before <paramref name="from"/>.
    /// When <paramref name="from"/> sits inside a block, that is the block before the current one.
    /// </summary>
    public static int? PrevDifference(IReadOnlyList<DiffRow> rows, int from)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var index = from;
        if (index > rows.Count)
        {
            index = rows.Count;
        }

        if (index < 0)
        {
            index = 0;
        }

        while (index > 0
            && index < rows.Count
            && rows[index].Kind != RowKind.Equal
            && rows[index - 1].Kind != RowKind.Equal)
        {
            index--;
        }

        for (var k = index - 1; k >= 0; k--)
        {
            if (IsBlockStart(rows, k))
            {
                return k;
            }
        }

        return null;
    }

    private static List<DiffRow> RowsFrom(
        IReadOnlyList<string> leftLines,
        IReadOnlyList<string> rightLines,
        IList<DiffBlock> blocks)
    {
        var rows = new List<DiffRow>();
        var leftIndex = 0;
        var rightIndex = 0;
        foreach (var block in blocks)
        {
            AddEqual(rows, leftLines, rightLines, ref leftIndex, ref rightIndex, block.DeleteStartA, block.InsertStartB);
            AddChange(rows, leftLines, rightLines, block);
            leftIndex = block.DeleteStartA + block.DeleteCountA;
            rightIndex = block.InsertStartB + block.InsertCountB;
        }

        AddEqual(rows, leftLines, rightLines, ref leftIndex, ref rightIndex, leftLines.Count, rightLines.Count);
        return rows;
    }

    private static void AddEqual(
        List<DiffRow> rows,
        IReadOnlyList<string> leftLines,
        IReadOnlyList<string> rightLines,
        ref int leftIndex,
        ref int rightIndex,
        int leftEnd,
        int rightEnd)
    {
        while (leftIndex < leftEnd && rightIndex < rightEnd)
        {
            rows.Add(new DiffRow(
                RowKind.Equal,
                leftIndex + 1,
                leftLines[leftIndex],
                rightIndex + 1,
                rightLines[rightIndex]));
            leftIndex++;
            rightIndex++;
        }
    }

    private static void AddChange(
        List<DiffRow> rows,
        IReadOnlyList<string> leftLines,
        IReadOnlyList<string> rightLines,
        DiffBlock block)
    {
        var count = Math.Max(block.DeleteCountA, block.InsertCountB);
        for (var k = 0; k < count; k++)
        {
            rows.Add(ChangeRow(leftLines, rightLines, block, k));
        }
    }

    private static DiffRow ChangeRow(
        IReadOnlyList<string> leftLines,
        IReadOnlyList<string> rightLines,
        DiffBlock block,
        int offset)
    {
        var hasLeft = offset < block.DeleteCountA;
        var hasRight = offset < block.InsertCountB;
        var kind = RowKind.RightOnly;
        if (hasLeft && hasRight)
        {
            kind = RowKind.Changed;
        }
        else if (hasLeft)
        {
            kind = RowKind.LeftOnly;
        }

        var leftAt = block.DeleteStartA + offset;
        var rightAt = block.InsertStartB + offset;
        return new DiffRow(
            kind,
            hasLeft ? leftAt + 1 : null,
            hasLeft ? leftLines[leftAt] : null,
            hasRight ? rightAt + 1 : null,
            hasRight ? rightLines[rightAt] : null);
    }

    private sealed class NormalizedChunker : IChunker
    {
        private readonly DiffOptions _options;

        public NormalizedChunker(DiffOptions options) => _options = options;

        public IReadOnlyList<string> Chunk(string text)
        {
            var lines = TextLines.Split(text);
            var normalized = new string[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                normalized[i] = TextLines.Normalize(lines[i], _options);
            }

            return normalized;
        }
    }
}
