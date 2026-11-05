namespace D3skCommander.Core.Sync;

/// <summary>How the two sides of one relative path compare.</summary>
public enum SyncStatus
{
    Equal,
    LeftOnly,
    RightOnly,
    LeftNewer,
    RightNewer,

    /// <summary>Present on both sides and different, with no date that says which one wins.</summary>
    Different,
}
