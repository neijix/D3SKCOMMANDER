using D3skCommander.Core.Sync;
using Xunit;

namespace D3skCommander.Core.Tests.Sync;

/// <summary>P25: the seven tests of <c>src/ui/sync_view.rs</c>.</summary>
public class SyncPlanTests
{
    [Fact]
    public void Symbols()
    {
        Assert.Equal("=", SyncPlan.StatusSymbol(SyncStatus.Equal));
        Assert.Equal("\u2260", SyncPlan.StatusSymbol(SyncStatus.Different));
        Assert.Equal("\u2192", SyncPlan.StatusSymbol(SyncStatus.LeftNewer));
        Assert.Equal("\u2192", SyncPlan.StatusSymbol(SyncStatus.LeftOnly));
        Assert.Equal("\u2190", SyncPlan.StatusSymbol(SyncStatus.RightNewer));
        Assert.Equal("\u2190", SyncPlan.StatusSymbol(SyncStatus.RightOnly));
    }

    [Fact]
    public void ValidActionsPerStatus()
    {
        Assert.Equal(new[] { SyncAction.None }, SyncPlan.ValidActions(SyncStatus.Equal));
        Assert.Equal(
            new[] { SyncAction.None, SyncAction.CopyToRight, SyncAction.DeleteLeft },
            SyncPlan.ValidActions(SyncStatus.LeftOnly));
        Assert.Equal(
            new[] { SyncAction.None, SyncAction.CopyToLeft, SyncAction.DeleteRight },
            SyncPlan.ValidActions(SyncStatus.RightOnly));
        Assert.DoesNotContain(SyncAction.DeleteLeft, SyncPlan.ValidActions(SyncStatus.Different));
        Assert.Contains(SyncAction.CopyToLeft, SyncPlan.ValidActions(SyncStatus.RightNewer));
    }

    [Fact]
    public void DefaultActionsAreAlwaysValid()
    {
        var defaults = new (SyncStatus Status, SyncAction Action)[]
        {
            (SyncStatus.LeftOnly, SyncAction.CopyToRight),
            (SyncStatus.RightOnly, SyncAction.CopyToLeft),
            (SyncStatus.LeftNewer, SyncAction.CopyToRight),
            (SyncStatus.RightNewer, SyncAction.CopyToLeft),
        };
        foreach (var row in defaults)
        {
            Assert.Equal(row.Action, DirectorySync.DefaultAction(row.Status, asymmetric: false));
            Assert.Contains(row.Action, SyncPlan.ValidActions(row.Status));
        }
    }

    [Fact]
    public void FilteringByClass()
    {
        var items = Sample();
        var filter = new SyncFilter();
        Assert.Equal(new[] { 1, 2, 3, 4 }, SyncPlan.FilterIndices(items, filter));
        filter.Equal = true;
        filter.LeftOnly = false;
        Assert.Equal(new[] { 0, 1, 3, 4 }, SyncPlan.FilterIndices(items, filter));
        filter.Newer = false;
        filter.RightOnly = false;
        Assert.Equal(new[] { 0 }, SyncPlan.FilterIndices(items, filter));
    }

    [Fact]
    public void CountsPerClass()
    {
        Assert.Equal(new[] { 1, 2, 1, 1 }, SyncPlan.CountClasses(Sample()));
    }

    [Fact]
    public void FeedUsesTheSideThatExists()
    {
        Assert.Equal(
            new[]
            {
                Path.Combine("/L", "ln"),
                Path.Combine("/L", "lo"),
                Path.Combine("/R", "ro"),
                Path.Combine("/L", "df"),
            },
            SyncPlan.FeedPaths(Sample(), "/L", "/R"));
    }

    [Fact]
    public void PendingCountsNonSkipped()
    {
        var items = Sample();
        items[1].Action = SyncAction.CopyToRight;
        items[3].Action = SyncAction.DeleteRight;
        Assert.Equal(2, SyncPlan.PendingCount(items));
    }

    private static SyncItem[] Sample()
    {
        return
        [
            Item("eq", SyncStatus.Equal, left: true, right: true),
            Item("ln", SyncStatus.LeftNewer, left: true, right: true),
            Item("lo", SyncStatus.LeftOnly, left: true, right: false),
            Item("ro", SyncStatus.RightOnly, left: false, right: true),
            Item("df", SyncStatus.Different, left: true, right: true),
        ];
    }

    private static SyncItem Item(string rel, SyncStatus status, bool left, bool right)
    {
        var side = new SyncSide(1, null);
        return new SyncItem(rel, false, left ? side : null, right ? side : null, status, SyncAction.None);
    }
}
