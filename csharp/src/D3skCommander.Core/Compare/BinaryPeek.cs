namespace D3skCommander.Core.Compare;

/// <summary>Bytes of both files around the first difference.</summary>
public sealed class BinaryPeek
{
    public BinaryPeek(long start, long first, byte[] left, byte[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Start = start;
        First = first;
        Left = left;
        Right = right;
    }

    public long Start { get; }

    public long First { get; }

    public byte[] Left { get; }

    public byte[] Right { get; }
}
