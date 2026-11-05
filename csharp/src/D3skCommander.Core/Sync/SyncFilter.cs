namespace D3skCommander.Core.Sync;

/// <summary>The four status classes the review shows. Equal is hidden until asked for.</summary>
public sealed class SyncFilter
{
    public bool Equal { get; set; }

    public bool Newer { get; set; } = true;

    public bool LeftOnly { get; set; } = true;

    public bool RightOnly { get; set; } = true;

    public bool Shows(SyncStatus status)
    {
        if (status == SyncStatus.Equal)
        {
            return Equal;
        }

        if (status == SyncStatus.LeftOnly)
        {
            return LeftOnly;
        }

        if (status == SyncStatus.RightOnly)
        {
            return RightOnly;
        }

        return Newer;
    }
}
