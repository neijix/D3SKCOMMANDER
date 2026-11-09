using Avalonia.Media;
using D3skCommander.App.Features.DiskUsage;
using D3skCommander.App.Features.FileOps;
using D3skCommander.Core.DiskUsage;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.App.Tests.Features.DiskUsage;

public partial class DiskUsageWindowTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task DeleteRemovesTheRowAndUpdatesTheTotalsWithoutScanningAgain()
    {
        using var tree = new UsageTree();
        var deleter = new DiskDeleter(remove: true);
        var model = new DiskUsageViewModel(English(), new DiskPanels(tree.Root, null), deleter);
        await model.BeginAsync(tree.Root).WaitAsync(Wait, TestContext.Current.CancellationToken);
        model.Selected = model.Rows.Single(row => row.Node.Name == "mid");
        Assert.True(model.CanDeleteSelected);

        Assert.True(await model.DeleteSelectedAsync());

        Assert.Equal([Path.Combine(tree.Root, "mid")], deleter.Asked);
        Assert.Equal(["big", "small.txt"], model.Rows.Select(row => row.Node.Name).ToArray());
        Assert.Equal(2010, model.Root!.Bytes);
        Assert.Equal("small.txt", model.Selected!.Node.Name);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ACancelledDeleteOrTheSmallerFilesRowLeavesTheListAlone()
    {
        using var tree = new UsageTree();
        var model = new DiskUsageViewModel(English(), new DiskPanels(tree.Root, null), new DiskDeleter(remove: false));
        await model.BeginAsync(tree.Root).WaitAsync(Wait, TestContext.Current.CancellationToken);
        model.Selected = model.Rows[0];

        Assert.False(await model.DeleteSelectedAsync());
        Assert.Equal(3, model.Rows.Count);

        var withoutDeleter = new DiskUsageViewModel(English(), new DiskPanels(tree.Root, null));
        await withoutDeleter.BeginAsync(tree.Root).WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.False(withoutDeleter.CanDeleteSelected);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task EachListBarTakesTheColourOfItsRingSegment()
    {
        using var tree = new UsageTree();
        var model = new DiskUsageViewModel(English(), new DiskPanels(tree.Root, null));
        await model.BeginAsync(tree.Root).WaitAsync(Wait, TestContext.Current.CancellationToken);

        var firsts = model.Segments.Where(segment => segment.Level == 1).ToList();
        foreach (var row in model.Rows)
        {
            var at = firsts.FindIndex(segment => ReferenceEquals(segment.Node, row.Node));
            var expected = at < 0 ? DiskUsageHeat.Other : DiskUsageHeat.ForBranch(at, 1);
            Assert.Equal(expected, ((ISolidColorBrush)row.Swatch).Color);
        }

        Assert.Equal("big", model.LabelOf(firsts[0]));
    }

    [Fact]
    public void ANameFitsInAWideSegmentButNotInASliver()
    {
        var wide = new RingSegment("/a", 1, 0, 120, null, false, 1);
        var sliver = new RingSegment("/b", 1, 0, 2, null, false, 1);

        Assert.True(RingChart.Fits(wide, 40, 30, 30, 12));
        Assert.False(RingChart.Fits(sliver, 40, 30, 30, 12));
    }

    private static ILocalizer English() => Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);

    /// <summary>Deletes from disk when <c>remove</c>, else answers as a cancelled confirmation would.</summary>
    private sealed class DiskDeleter(bool remove) : IPathDeleter
    {
        public List<string> Asked { get; } = [];

        public Task<bool> DeletePathsAsync(IReadOnlyList<string> paths)
        {
            Asked.AddRange(paths);
            if (!remove)
            {
                return Task.FromResult(false);
            }

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    File.Delete(path);
                }
            }

            return Task.FromResult(true);
        }
    }
}
