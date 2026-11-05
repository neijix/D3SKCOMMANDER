namespace D3skCommander.Core.Sync;

/// <summary>One scanned file or directory on one side.</summary>
internal sealed class SyncEntry
{
    public SyncEntry(bool isDir, SyncSide side, bool empty)
    {
        ArgumentNullException.ThrowIfNull(side);
        IsDir = isDir;
        Side = side;
        Empty = empty;
    }

    public bool IsDir { get; }

    public SyncSide Side { get; }

    /// <summary>Directories only, and only computed when empty folders are listed.</summary>
    public bool Empty { get; }
}
