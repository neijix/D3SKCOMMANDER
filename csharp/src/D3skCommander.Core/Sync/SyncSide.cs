namespace D3skCommander.Core.Sync;

/// <summary>Size and modification time of one side. A directory's size is zero.</summary>
public sealed class SyncSide
{
    public SyncSide(ulong size, DateTimeOffset? modified)
    {
        Size = size;
        Modified = modified;
    }

    public ulong Size { get; }

    public DateTimeOffset? Modified { get; }
}
