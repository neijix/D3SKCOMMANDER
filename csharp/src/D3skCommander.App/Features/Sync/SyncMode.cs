using D3skCommander.Core.Sync;

namespace D3skCommander.App.Features.Sync;

/// <summary>Which side wins. Symmetric copies both ways, the others make one side a mirror of the other.</summary>
public enum SyncMode
{
    Symmetric,
    LeftToRight,
    RightToLeft,
}

/// <summary>How the criteria choice maps onto the scan options.</summary>
public enum SyncCriteria
{
    Date,
    Size,
    Content,
}

/// <summary>Turns the mode and criteria choices into scan options and default actions.</summary>
public static class SyncChoices
{
    public static SyncOptions Options(SyncMode mode, SyncCriteria criteria, bool emptyDirs)
    {
        return new SyncOptions
        {
            Asymmetric = mode == SyncMode.LeftToRight,
            ByContent = criteria == SyncCriteria.Content,
            IgnoreDate = criteria == SyncCriteria.Size,
            EmptyDirs = emptyDirs,
        };
    }

    /// <summary>Right to left mirrors the right side onto the left one, from a symmetric scan.</summary>
    public static void ApplyMode(IReadOnlyList<SyncItem> items, SyncMode mode)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (mode != SyncMode.RightToLeft)
        {
            return;
        }

        foreach (var item in items)
        {
            item.Action = MirrorLeft(item.Status);
        }
    }

    private static SyncAction MirrorLeft(SyncStatus status)
    {
        if (status == SyncStatus.Equal)
        {
            return SyncAction.None;
        }

        if (status == SyncStatus.LeftOnly)
        {
            return SyncAction.DeleteLeft;
        }

        return SyncAction.CopyToLeft;
    }
}
