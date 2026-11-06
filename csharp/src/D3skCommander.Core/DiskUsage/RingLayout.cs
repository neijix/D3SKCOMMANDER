namespace D3skCommander.Core.DiskUsage;

/// <summary>Where each descendant of a node sits on the ring chart. Pure: no drawing.</summary>
public static class RingLayout
{
    public const int MaxLevels = 5;

    /// <summary>Children below this share of their parent merge into one "other" segment.</summary>
    public const double MinShare = 0.01;

    /// <summary>
    /// The segments of <paramref name="center"/>'s descendants, down to <paramref name="levels"/> rings, level by level.
    /// The children of a node share its sweep in proportion to their size, so they add up to it.
    /// </summary>
    public static IReadOnlyList<RingSegment> Build(SizeNode center, int levels = MaxLevels, double minShare = MinShare)
    {
        ArgumentNullException.ThrowIfNull(center);
        var all = new List<RingSegment>();
        Expand(center, 1, 0, 360, Math.Clamp(levels, 0, MaxLevels), minShare, all);
        return all.OrderBy(segment => segment.Level).ThenBy(segment => segment.StartAngle).ToList();
    }

    private static void Expand(SizeNode node, int level, double start, double sweep, int levels, double minShare, List<RingSegment> into)
    {
        if (level > levels)
        {
            return;
        }

        var shown = node.Children.Where(child => child.Bytes > 0).ToList();
        var total = shown.Sum(child => child.Bytes);
        if (total <= 0)
        {
            return;
        }

        var big = shown.Where(child => (double)child.Bytes / total >= minShare).ToList();
        var small = shown.Where(child => (double)child.Bytes / total < minShare).ToList();
        if (small.Count == 1)
        {
            big.Add(small[0]);
            small.Clear();
        }

        var at = start;
        foreach (var child in big)
        {
            var part = sweep * child.Bytes / total;
            into.Add(new RingSegment(child.Path, level, at, part, child, false, child.Bytes));
            if (child.Kind == SizeNodeKind.Folder)
            {
                Expand(child, level + 1, at, part, levels, minShare, into);
            }

            at += part;
        }

        if (small.Count > 0)
        {
            var bytes = small.Sum(child => child.Bytes);
            into.Add(new RingSegment(node.Path, level, at, sweep * bytes / total, null, true, bytes, small.Count));
        }
    }
}
