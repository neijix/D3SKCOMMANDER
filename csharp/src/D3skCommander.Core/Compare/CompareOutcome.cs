namespace D3skCommander.Core.Compare;

/// <summary>The last compare a session has applied. An error is inline: the session stays open.</summary>
public sealed class CompareOutcome
{
    private CompareOutcome(
        CompareKind kind,
        IReadOnlyList<DiffRow> rows,
        int blocks,
        bool identical,
        long? firstDifference,
        BinaryPeek? peek,
        string? error)
    {
        Kind = kind;
        Rows = rows;
        Blocks = blocks;
        Identical = identical;
        FirstDifference = firstDifference;
        Peek = peek;
        Error = error;
    }

    public CompareKind Kind { get; }

    public IReadOnlyList<DiffRow> Rows { get; }

    public int Blocks { get; }

    public bool Identical { get; }

    public long? FirstDifference { get; }

    public BinaryPeek? Peek { get; }

    public string? Error { get; }

    public static CompareOutcome None { get; } = new(CompareKind.None, [], 0, false, null, null, null);

    public static CompareOutcome Text(IReadOnlyList<DiffRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new CompareOutcome(CompareKind.Text, rows, DiffNavigator.BlockCount(rows), false, null, null, null);
    }

    public static CompareOutcome Binary(bool identical, long? firstDifference, BinaryPeek? peek) =>
        new(CompareKind.Binary, [], 0, identical, firstDifference, peek, null);

    public static CompareOutcome Failed(string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        return new CompareOutcome(CompareKind.Error, [], 0, false, null, null, message);
    }
}
