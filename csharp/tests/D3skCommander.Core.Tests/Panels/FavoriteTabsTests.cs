using System.IO.Compression;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class FavoriteTabsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("d3sk-fav-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void ASavedFileComesBackWithEveryField()
    {
        var folder = Path.Combine(_root, "cfg");
        var favorites = new FavoriteTabs();
        favorites.Save(Set("Work", Side(1, ("/a", SortKey.Size, "Details"), ("/b", SortKey.Name, "Default")), Side(0, ("/c", SortKey.Modified, "Default")), FavoriteSide.Right));
        using (var store = new FavoriteTabsStore(folder))
        {
            store.Save(favorites);
        }

        using var reread = new FavoriteTabsStore(folder);

        var set = Assert.Single(reread.Current.Sets);
        Assert.Equal("Work", set.Name);
        Assert.Equal(FavoriteSide.Right, set.ActiveSide);
        Assert.Equal(1, set.Left.ActiveIndex);
        Assert.Equal(["/a", "/b"], set.Left.Tabs.Select(t => t.Directory));
        Assert.Equal(new SortOrder(SortKey.Size, true), set.Left.Tabs[0].Sort);
        Assert.Equal("Details", set.Left.Tabs[0].ColumnSet);
        Assert.Equal(PanelViewMode.Brief, set.Right.Tabs[0].ViewMode);
        Assert.True(File.Exists(Path.Combine(folder, "favorite-tabs.json")));
    }

    [Theory]
    [InlineData("{ not json at all")]
    [InlineData("[1, 2]")]
    [InlineData("{\"sets\": 5}")]
    public void AMalformedFileGivesAnEmptyList(string text)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "bad")).FullName;
        File.WriteAllText(Path.Combine(folder, "favorite-tabs.json"), text);

        using var store = new FavoriteTabsStore(folder);

        Assert.Empty(store.Current.Sets);
    }

    [Fact]
    public void ANamelessOrRepeatedEntryInTheFileIsDropped()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "dirty")).FullName;
        File.WriteAllText(
            Path.Combine(folder, "favorite-tabs.json"),
            """{"schema":1,"sets":[null,{"name":""},{"name":"A"},{"name":"a"},{"name":"B","left":null}]}""");

        using var store = new FavoriteTabsStore(folder);

        Assert.Equal(["A", "B"], store.Current.Sets.Select(s => s.Name));
    }

    [Fact]
    public void SavingTheSameNameReplacesTheSetInPlace()
    {
        var favorites = new FavoriteTabs();
        Assert.False(favorites.Save(Set("One", Side(0, ("/a", SortKey.Name, "Default")))));
        favorites.Save(Set("Two", Side(0, ("/b", SortKey.Name, "Default"))));

        var replaced = favorites.Save(Set("ONE", Side(0, ("/z", SortKey.Name, "Default"))));

        Assert.True(replaced);
        Assert.Equal(["ONE", "Two"], favorites.Sets.Select(s => s.Name));
        Assert.Equal("/z", favorites.Sets[0].Left.Tabs[0].Directory);
        Assert.NotNull(favorites.Find("one"));
    }

    [Fact]
    public void RenameDeleteAndMoveEditTheList()
    {
        var favorites = new FavoriteTabs();
        foreach (var name in new[] { "A", "B", "C" })
        {
            favorites.Save(Set(name, Side(0, ("/" + name, SortKey.Name, "Default"))));
        }

        Assert.False(favorites.Rename("A", "b"));
        Assert.False(favorites.Rename("A", "  "));
        Assert.False(favorites.Rename("nope", "X"));
        Assert.True(favorites.Rename("A", " Alpha "));
        Assert.True(favorites.Rename("B", "b"));
        Assert.Equal(["Alpha", "b", "C"], favorites.Sets.Select(s => s.Name));

        Assert.True(favorites.Move("C", -1));
        Assert.False(favorites.Move("Alpha", -1));
        Assert.False(favorites.Move("b", 1));
        Assert.True(favorites.Move("Alpha", 1));
        Assert.Equal(["C", "Alpha", "b"], favorites.Sets.Select(s => s.Name));

        Assert.True(favorites.Delete("alpha"));
        Assert.False(favorites.Delete("alpha"));
        Assert.Equal(["C", "b"], favorites.Sets.Select(s => s.Name));
    }

    [Fact]
    public void ACloneIsIndependent()
    {
        var favorites = new FavoriteTabs();
        favorites.Save(Set("A", Side(0, ("/a", SortKey.Name, "Default"))));

        var copy = favorites.Clone();
        copy.Sets[0].Left.Tabs[0].Directory = "/changed";
        copy.Delete("A");

        Assert.Equal("/a", favorites.Sets[0].Left.Tabs[0].Directory);
    }

    [Fact]
    public async Task CaptureKeepsLocalTabsOnlyAndTheActiveSide()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "plain")).FullName;
        var zip = Path.Combine(_root, "pack.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("inner/file.txt");
        }

        var local = new LocalFileSystemProvider();
        var results = new ListFileSystemProvider(folder, []);
        var tabs = new List<Tab>
        {
            new(new Location(folder), new SortOrder(SortKey.Size, true), local) { ColumnSetName = "Details" },
            new(new Location(zip), SortOrder.NameAscending, local),
            new(new Location(folder, results), SortOrder.NameAscending, local),
            new(new Location("ftp://host/dir"), SortOrder.NameAscending, local),
            new(new Location(_root), SortOrder.NameAscending, local),
        };

        var side = FavoriteTabsPlan.CaptureSide(tabs, active: 4, PanelViewMode.Brief);

        Assert.Equal([folder, _root], side.Tabs.Select(t => t.Directory));
        Assert.Equal(1, side.ActiveIndex);
        Assert.Equal("Details", side.Tabs[0].ColumnSet);
        Assert.Equal(new SortOrder(SortKey.Size, true), side.Tabs[0].Sort);
        Assert.All(side.Tabs, t => Assert.Equal(PanelViewMode.Brief, t.ViewMode));

        var skipped = FavoriteTabsPlan.CaptureSide(tabs, active: 1, PanelViewMode.Full);
        Assert.Equal(0, skipped.ActiveIndex);

        var left = await Panel.OpenAsync([folder], 0, SortOrder.NameAscending, false, local, TestContext.Current.CancellationToken);
        var right = await Panel.OpenAsync([_root, folder], 1, SortOrder.NameAscending, false, local, TestContext.Current.CancellationToken);
        var set = FavoriteTabsPlan.Capture(" Mine ", left, right, PanelViewMode.Full, PanelViewMode.Thumbnails, FavoriteSide.Right);
        Assert.Equal("Mine", set.Name);
        Assert.Equal(FavoriteSide.Right, set.ActiveSide);
        Assert.Equal(1, set.Right.ActiveIndex);
        Assert.Equal(PanelViewMode.Thumbnails, set.Right.Tabs[0].ViewMode);
    }

    [Fact]
    public void ReplaceModeOpensTheSetInsteadOfTheOpenTabs()
    {
        var set = Set("W", Side(1, ("/a", SortKey.Name, "Default"), ("/b", SortKey.Size, "Details")), Side(0, ("/c", SortKey.Name, "Default")), FavoriteSide.Right);

        var plan = FavoriteTabsPlan.Restore(set, FavoriteRestoreMode.Replace, 3, 2, _ => true, "/home");

        Assert.False(plan.Left.KeepOpen);
        Assert.Equal(["/a", "/b"], plan.Left.Open.Select(t => t.Path));
        Assert.Equal(1, plan.Left.Active);
        Assert.Equal(new SortOrder(SortKey.Size, true), plan.Left.Open[1].Sort);
        Assert.Equal("Details", plan.Left.Open[1].ColumnSet);
        Assert.Equal(["/c"], plan.Right.Open.Select(t => t.Path));
        Assert.Equal(FavoriteSide.Right, plan.ActiveSide);
        Assert.Equal(PanelViewMode.Brief, plan.Left.View);
    }

    [Fact]
    public void AddModeKeepsTheOpenTabsAndTheActiveTabIsInTheResultList()
    {
        var set = Set("W", Side(1, ("/a", SortKey.Name, "Default"), ("/b", SortKey.Name, "Default")), Side(0));

        var plan = FavoriteTabsPlan.Restore(set, FavoriteRestoreMode.Add, 3, 2, _ => true, "/home");

        Assert.True(plan.Left.KeepOpen);
        Assert.Equal(["/a", "/b"], plan.Left.Open.Select(t => t.Path));
        Assert.Equal(4, plan.Left.Active);
        Assert.True(plan.Right.KeepOpen);
        Assert.Empty(plan.Right.Open);
        Assert.Equal(-1, plan.Right.Active);
        Assert.Null(plan.Right.View);
    }

    [Fact]
    public void ComputerIsCapturedAsLocalAndRestoredWithoutAnExistsCheck()
    {
        var local = new LocalFileSystemProvider();
        var tabs = new List<Tab> { new(new Location(ComputerAddress.Text), SortOrder.NameAscending, local) };

        var side = FavoriteTabsPlan.CaptureSide(tabs, active: 0, PanelViewMode.Full);
        var set = Set("W", side, Side(0));
        var plan = FavoriteTabsPlan.Restore(set, FavoriteRestoreMode.Replace, 1, 1, _ => false, "/home");

        Assert.Equal([ComputerAddress.Text], side.Tabs.Select(t => t.Directory));
        Assert.Equal([ComputerAddress.Text], plan.Left.Open.Select(t => t.Path));
        Assert.Equal(0, plan.Left.Active);
    }

    [Fact]
    public void AMissingFolderIsSkippedAndTheActiveTabFollowsItsFolder()
    {
        var set = Set("W", Side(2, ("/a", SortKey.Name, "Default"), ("/gone", SortKey.Name, "Default"), ("/c", SortKey.Name, "Default")), Side(0));

        var plan = FavoriteTabsPlan.Restore(set, FavoriteRestoreMode.Replace, 1, 1, path => path != "/gone", "/home");

        Assert.Equal(["/a", "/c"], plan.Left.Open.Select(t => t.Path));
        Assert.Equal(1, plan.Left.Active);
    }

    [Fact]
    public void ASideLeftEmptyOpensHomeInReplaceMode()
    {
        var set = Set("W", Side(0, ("/gone", SortKey.Name, "Default")), Side(0));

        var plan = FavoriteTabsPlan.Restore(set, FavoriteRestoreMode.Replace, 2, 2, _ => false, "/home");

        Assert.False(plan.Left.KeepOpen);
        Assert.Equal(["/home"], plan.Left.Open.Select(t => t.Path));
        Assert.Equal(0, plan.Left.Active);
        Assert.Equal(["/home"], plan.Right.Open.Select(t => t.Path));
    }

    [Fact]
    public async Task APanelOpensAndAddsStartsWithTheirSortAndColumnSet()
    {
        var a = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
        var local = new LocalFileSystemProvider();
        var token = TestContext.Current.CancellationToken;
        var panel = await Panel.OpenStartsAsync(
            [new FavoriteTabStart(a, new SortOrder(SortKey.Size, true), "Details"), new FavoriteTabStart(b, SortOrder.NameAscending, "Default")],
            1,
            false,
            local,
            token);

        Assert.Equal(1, panel.ActiveTab);
        Assert.Equal(new SortOrder(SortKey.Size, true), panel.Tabs[0].Sort);
        Assert.Equal("Details", panel.Tabs[0].ColumnSetName);

        await panel.AddStartsAsync([new FavoriteTabStart(_root, SortOrder.NameAscending, "Default")], 2, false, token);
        Assert.Equal([a, b, _root], panel.TabDirectories());
        Assert.Equal(2, panel.ActiveTab);

        await panel.AddStartsAsync([], -1, false, token);
        Assert.Equal(2, panel.ActiveTab);
    }

    private static FavoriteTabSet Set(string name, SessionPanel left, SessionPanel? right = null, FavoriteSide active = FavoriteSide.Left) =>
        new() { Name = name, Left = left, Right = right ?? new SessionPanel(), ActiveSide = active };

    private static SessionPanel Side(int active, params (string Path, SortKey Key, string Set)[] tabs)
    {
        var panel = new SessionPanel { ActiveIndex = active };
        foreach (var (path, key, set) in tabs)
        {
            panel.Tabs.Add(new SessionTab
            {
                Directory = path,
                Sort = new SortOrder(key, key == SortKey.Size),
                ViewMode = PanelViewMode.Brief,
                ColumnSet = set,
            });
        }

        return panel;
    }
}
