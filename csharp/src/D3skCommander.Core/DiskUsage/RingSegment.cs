namespace D3skCommander.Core.DiskUsage;

/// <summary>
/// One arc of the ring chart. Level 1 is the ring around the centre. Angles are degrees, 0 at twelve o'clock,
/// growing clockwise. An "other" segment merges small siblings: it has no <see cref="Node"/>.
/// </summary>
public sealed record RingSegment(
    string Path,
    int Level,
    double StartAngle,
    double Sweep,
    SizeNode? Node,
    bool IsOther,
    long Bytes,
    int Merged = 0)
{
    public double EndAngle => StartAngle + Sweep;
}
