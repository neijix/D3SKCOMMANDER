using D3skCommander.Core.DiskUsage;
using Xunit;

namespace D3skCommander.Core.Tests.DiskUsage;

public class RingLayoutTests
{
    private static SizeNode File(string name, long bytes) => SizeNode.File(name, "/r/" + name, bytes);

    private static SizeNode Folder(string name, params SizeNode[] children) => SizeNode.Folder(name, "/r/" + name, children);

    [Fact]
    public void PercentAndHeat()
    {
        Assert.Equal(25, SizeShares.Percent(25, 100));
        Assert.Equal(0, SizeShares.Percent(5, 0));
        Assert.Equal(100, SizeShares.Percent(500, 100));
        Assert.Equal("25 %", SizeShares.PercentText(25, 100));
        Assert.Equal("12.3 %", SizeShares.PercentText(123, 1000));
        Assert.Equal("< 0.1 %", SizeShares.PercentText(1, 10_000));
        Assert.Equal(0, SizeShares.HeatStep(0));
        Assert.Equal(0, SizeShares.HeatStep(0.05));
        Assert.Equal(SizeShares.HeatSteps - 1, SizeShares.HeatStep(1));
        Assert.Equal(SizeShares.HeatSteps - 1, SizeShares.HeatStep(7));
        Assert.Equal(3, SizeShares.HeatStep(0.5));
        Assert.True(SizeShares.HeatStep(0.2) < SizeShares.HeatStep(0.8));
    }

    [Fact]
    public void FolderTotalsAreTheSumOfTheirChildren()
    {
        var tree = Folder("top", File("a", 10), Folder("sub", File("b", 5), SizeNode.SmallFiles("/r/sub", 4, 6)));

        Assert.Equal(21, tree.Bytes);
        Assert.Equal(6, tree.Files);
        Assert.Equal(1, tree.Folders);
        Assert.Equal(["sub", "a"], tree.Children.Select(child => child.Name).ToArray());
    }

    [Fact]
    public void ChildrenShareTheirParentsSweep()
    {
        var tree = Folder("top", File("a", 500), Folder("sub", File("b", 250), File("c", 250)));

        var segments = RingLayout.Build(tree);

        var first = segments.Where(segment => segment.Level == 1).ToList();
        Assert.Equal(360, first.Sum(segment => segment.Sweep), 6);
        Assert.Equal(0, first[0].StartAngle);
        Assert.Equal(first[0].EndAngle, first[1].StartAngle, 6);
        var second = segments.Where(segment => segment.Level == 2).ToList();
        var sub = Assert.Single(first, segment => segment.Path == "/r/sub");
        Assert.Equal(sub.Sweep, second.Sum(segment => segment.Sweep), 6);
        Assert.All(second, segment => Assert.InRange(segment.StartAngle, sub.StartAngle - 1e-9, sub.EndAngle + 1e-9));
    }

    [Fact]
    public void SmallSiblingsMergeIntoOneOtherSegment()
    {
        var small = Enumerable.Range(0, 5).Select(i => File("s" + i, 1)).ToArray();
        var tree = Folder("top", [File("big", 10_000), .. small]);

        var segments = RingLayout.Build(tree);

        Assert.Equal(2, segments.Count);
        var other = Assert.Single(segments, segment => segment.IsOther);
        Assert.Equal(5, other.Bytes);
        Assert.Equal(5, other.Merged);
        Assert.Null(other.Node);
        Assert.Equal(360, segments.Sum(segment => segment.Sweep), 6);
    }

    [Fact]
    public void ALoneSmallSiblingStaysItself()
    {
        var tree = Folder("top", File("big", 10_000), File("tiny", 1));

        var segments = RingLayout.Build(tree);

        Assert.DoesNotContain(segments, segment => segment.IsOther);
        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void RingsStopAtFiveLevels()
    {
        var node = File("leaf", 10);
        for (var i = 0; i < 8; i++)
        {
            node = Folder("d" + i, node);
        }

        var segments = RingLayout.Build(node);

        Assert.Equal(5, segments.Max(segment => segment.Level));
        Assert.All(Enumerable.Range(1, 5), level => Assert.Equal(360, segments.Where(s => s.Level == level).Sum(s => s.Sweep), 6));
        Assert.Equal(2, RingLayout.Build(node, 2).Max(segment => segment.Level));
    }

    [Fact]
    public void EmptyFolderHasNoRings()
    {
        Assert.Empty(RingLayout.Build(Folder("empty")));
        Assert.Empty(RingLayout.Build(Folder("zero", File("z", 0))));
    }

    [Fact]
    public void HitTestFindsTheSegmentAtItsMiddleAndTheCentre()
    {
        var tree = Folder("top", File("a", 500), Folder("sub", File("b", 250), File("c", 250)));
        var segments = RingLayout.Build(tree);

        foreach (var segment in segments)
        {
            var (x, y) = RingGeometry.MiddleOf(segment, 40, 30);
            var hit = RingGeometry.Hit(segments, x, y, 40, 30);
            Assert.False(hit.IsCenter);
            Assert.Same(segment, hit.Segment);
        }

        Assert.True(RingGeometry.Hit(segments, 3, -4, 40, 30).IsCenter);
        Assert.Null(RingGeometry.Hit(segments, 500, 0, 40, 30).Segment);
    }
}
