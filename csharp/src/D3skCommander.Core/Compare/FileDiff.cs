namespace D3skCommander.Core.Compare;

/// <summary>A file compare: side-by-side text, or the first differing byte of a binary pair.</summary>
public sealed class FileDiff
{
    private FileDiff(bool isText, IReadOnlyList<DiffRow> rows, bool identical, long? firstDifference)
    {
        IsText = isText;
        Rows = rows;
        Identical = identical;
        FirstDifference = firstDifference;
    }

    public bool IsText { get; }

    public IReadOnlyList<DiffRow> Rows { get; }

    /// <summary>Set for a binary compare. True when the files are the same bytes.</summary>
    public bool Identical { get; }

    /// <summary>Offset of the first differing byte, or the shorter length when one file is a prefix. Null when identical.</summary>
    public long? FirstDifference { get; }

    public static FileDiff Text(IReadOnlyList<DiffRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new FileDiff(true, rows, false, null);
    }

    public static FileDiff Binary(bool identical, long? firstDifference) =>
        new(false, [], identical, firstDifference);
}
