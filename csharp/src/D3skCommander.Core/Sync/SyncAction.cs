namespace D3skCommander.Core.Sync;

/// <summary>What to do with one compared path. The review can change it before the plan runs.</summary>
public enum SyncAction
{
    None,
    CopyToRight,
    CopyToLeft,
    DeleteRight,
    DeleteLeft,
}
