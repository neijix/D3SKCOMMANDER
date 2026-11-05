namespace D3skCommander.Core.Compare;

/// <summary>How a side-by-side row relates its two sides.</summary>
public enum RowKind
{
    Equal,
    Changed,
    LeftOnly,
    RightOnly,
}
