namespace D3skCommander.Core.Sync;

/// <summary>One relative path in a sync plan. <see cref="Action"/> is what the review may change.</summary>
public sealed class SyncItem
{
    public SyncItem(string rel, bool isDir, SyncSide? left, SyncSide? right, SyncStatus status, SyncAction action)
    {
        ArgumentNullException.ThrowIfNull(rel);
        Rel = rel;
        IsDir = isDir;
        Left = left;
        Right = right;
        Status = status;
        Action = action;
    }

    /// <summary>Path relative to both roots, using the platform separator.</summary>
    public string Rel { get; }

    public bool IsDir { get; }

    public SyncSide? Left { get; }

    public SyncSide? Right { get; }

    public SyncStatus Status { get; }

    public SyncAction Action { get; set; }
}
