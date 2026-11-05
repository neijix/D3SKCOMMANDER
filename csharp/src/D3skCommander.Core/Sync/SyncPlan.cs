namespace D3skCommander.Core.Sync;

/// <summary>The reviewable plan: which actions a status allows, and the class filter, counts and paths.</summary>
public static class SyncPlan
{
    private static readonly SyncAction[] EqualActions = [SyncAction.None];

    private static readonly SyncAction[] LeftOnlyActions =
        [SyncAction.None, SyncAction.CopyToRight, SyncAction.DeleteLeft];

    private static readonly SyncAction[] RightOnlyActions =
        [SyncAction.None, SyncAction.CopyToLeft, SyncAction.DeleteRight];

    private static readonly SyncAction[] BothActions =
        [SyncAction.None, SyncAction.CopyToRight, SyncAction.CopyToLeft];

    public static IReadOnlyList<SyncAction> ValidActions(SyncStatus status)
    {
        if (status == SyncStatus.Equal)
        {
            return EqualActions;
        }

        if (status == SyncStatus.LeftOnly)
        {
            return LeftOnlyActions;
        }

        if (status == SyncStatus.RightOnly)
        {
            return RightOnlyActions;
        }

        return BothActions;
    }

    /// <summary>The mark v0.1 draws for a status: =, ≠, → or ←.</summary>
    public static string StatusSymbol(SyncStatus status)
    {
        if (status == SyncStatus.Equal)
        {
            return "=";
        }

        if (status == SyncStatus.Different)
        {
            return "\u2260";
        }

        if (status == SyncStatus.LeftNewer || status == SyncStatus.LeftOnly)
        {
            return "\u2192";
        }

        return "\u2190";
    }

    /// <summary>Counts as equal, newer or different, left only, right only.</summary>
    public static int[] CountClasses(IReadOnlyList<SyncItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var counts = new int[4];
        foreach (var item in items)
        {
            counts[ClassIndex(item.Status)]++;
        }

        return counts;
    }

    public static int[] FilterIndices(IReadOnlyList<SyncItem> items, SyncFilter filter)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(filter);
        var indices = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            if (filter.Shows(items[i].Status))
            {
                indices.Add(i);
            }
        }

        return indices.ToArray();
    }

    public static int PendingCount(IReadOnlyList<SyncItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var count = 0;
        foreach (var item in items)
        {
            if (item.Action != SyncAction.None)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Paths of every non-equal item: the left file, or the right one when only it exists.</summary>
    public static string[] FeedPaths(IReadOnlyList<SyncItem> items, string left, string right)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrEmpty(left);
        ArgumentException.ThrowIfNullOrEmpty(right);
        var paths = new List<string>();
        foreach (var item in items)
        {
            if (item.Status == SyncStatus.Equal)
            {
                continue;
            }

            var root = item.Left is not null ? left : right;
            paths.Add(Path.Combine(root, item.Rel));
        }

        return paths.ToArray();
    }

    private static int ClassIndex(SyncStatus status)
    {
        if (status == SyncStatus.Equal)
        {
            return 0;
        }

        if (status == SyncStatus.LeftOnly)
        {
            return 2;
        }

        if (status == SyncStatus.RightOnly)
        {
            return 3;
        }

        return 1;
    }
}
