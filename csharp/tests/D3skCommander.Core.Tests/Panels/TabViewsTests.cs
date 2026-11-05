using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class TabViewsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("d3sk-tabviews-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task SelectMaskMarksMatchingFilesAndUnselectDropsThem()
    {
        var tab = await Open("a.txt", "b.txt", "c.md");

        tab.SelectMask("*.txt", true, false);
        Assert.Equal(["a.txt", "b.txt"], Names(tab.Targets()));

        tab.SelectMask("a*", false, false);
        Assert.Equal(["b.txt"], Names(tab.Targets()));
    }

    [Fact]
    public async Task SameExtensionMarksEveryFileWithTheCursorExtension()
    {
        var tab = await Open("a.txt", "b.TXT", "c.md", "noext");
        tab.PlaceCursor(IndexOf(tab, "a.txt"));

        tab.SelectSameExtension();

        Assert.Equal(["a.txt", "b.TXT"], Names(tab.Targets()));
    }

    [Fact]
    public async Task TheFilterNarrowsTheListAndNullRestoresIt()
    {
        var tab = await Open("alpha.txt", "beta.txt", "gamma.md");

        tab.SetFilter("ta");
        Assert.Equal(["beta.txt"], Names(tab.VisibleEntries.Where(entry => !entry.IsParent)));

        tab.SetFilter(null);
        Assert.Equal(3, tab.VisibleEntries.Count(entry => !entry.IsParent));
    }

    [Fact]
    public async Task BranchListsNestedFilesByRelativeNameAndLeavesOnNavigation()
    {
        var tab = await Open("top.txt");
        Directory.CreateDirectory(Path.Combine(_root, "sub", "deep"));
        await File.WriteAllTextAsync(Path.Combine(_root, "sub", "deep", "x.txt"), "x", TestContext.Current.CancellationToken);

        await tab.SetBranchAsync(true, false, TestContext.Current.CancellationToken);

        var names = Names(tab.VisibleEntries.Where(entry => !entry.IsParent));
        Assert.Contains("top.txt", names);
        Assert.Contains(Path.Combine("sub", "deep", "x.txt"), names);
        Assert.DoesNotContain("sub", names);

        await tab.GoAsync(new Location(Path.Combine(_root, "sub")), false, TestContext.Current.CancellationToken);
        Assert.False(tab.Branch);
    }

    [Fact]
    public async Task FolderTargetsPreferMarkedFoldersAndSizesShowInTheEntry()
    {
        var tab = await Open("f.txt");
        Directory.CreateDirectory(Path.Combine(_root, "one"));
        Directory.CreateDirectory(Path.Combine(_root, "two"));
        await tab.ReloadAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal(["one", "two"], Names(tab.FolderTargets()));

        tab.ReplaceSelection([Path.Combine(_root, "two")]);
        Assert.Equal(["two"], Names(tab.FolderTargets()));

        tab.SetFolderSize(Path.Combine(_root, "two"), 1234);
        Assert.Equal(1234UL, tab.VisibleEntries.Single(entry => entry.Name == "two").Size);
        await tab.ReloadAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal(1234UL, tab.VisibleEntries.Single(entry => entry.Name == "two").Size);
    }

    [Fact]
    public async Task CopiedTextIsOneNameOrPathPerLine()
    {
        var tab = await Open("a.txt", "b.txt");
        tab.SelectAll();

        var targets = tab.Targets();

        Assert.Equal("a.txt\nb.txt", PanelText.Names(targets));
        Assert.Equal(Path.Combine(_root, "a.txt") + "\n" + Path.Combine(_root, "b.txt"), PanelText.Paths(targets));
    }

    private async Task<Tab> Open(params string[] files)
    {
        foreach (var file in files)
        {
            await File.WriteAllTextAsync(Path.Combine(_root, file), "x", TestContext.Current.CancellationToken);
        }

        var tab = new Tab(new Location(_root), SortOrder.NameAscending, new LocalFileSystemProvider());
        await tab.ReloadAsync(false, TestContext.Current.CancellationToken);
        return tab;
    }

    private static int IndexOf(Tab tab, string name) =>
        tab.VisibleEntries.ToList().FindIndex(entry => entry.Name == name);

    private static List<string> Names(IEnumerable<Entry> entries) =>
        entries.Select(entry => entry.Name).ToList();
}
