using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class PanelTests
{
    [Fact]
    public async Task NavigationHistoryAndCursorOnParent()
    {
        using var dir = new TempFolder();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        var stop = TestContext.Current.CancellationToken;
        var tab = await Open(dir.Path);
        var sub = tab.VisibleEntries.First(entry => entry.Name == "sub");
        var entered = tab.EnterLocation(sub);
        Assert.NotNull(entered);
        Assert.True(await tab.GoAsync(entered, showHidden: false, stop));
        Assert.Equal(Path.Combine(dir.Path, "sub"), tab.Location.Path);
        var up = tab.ParentLocation();
        Assert.NotNull(up);
        Assert.True(await tab.GoAsync(up, showHidden: false, stop));
        Assert.Equal("sub", tab.Current?.Name);
        await tab.BackAsync(showHidden: false, stop);
        Assert.Equal(Path.Combine(dir.Path, "sub"), tab.Location.Path);
        await tab.ForwardAsync(showHidden: false, stop);
        Assert.Equal(dir.Path, tab.Location.Path);
    }

    [Fact]
    public async Task GoToMissingFolderKeepsLocation()
    {
        using var dir = new TempFolder();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "b.txt"), "x");
        var tab = await Open(dir.Path);
        tab.SelectMask("*.txt", select: true, includeDirectories: false);
        tab.Filter = "*.txt";
        tab.ApplyFilter();
        tab.MoveCursor(1);
        var beforeCursor = tab.Cursor;
        var missing = Path.Combine(dir.Path, "nope");
        Assert.False(await tab.GoAsync(new Location(missing), showHidden: false, TestContext.Current.CancellationToken));
        Assert.Equal(dir.Path, tab.Location.Path);
        Assert.NotNull(tab.Error);
        Assert.Equal(ErrorKind.NotFound, tab.Error.Kind);
        Assert.Equal("*.txt", tab.Filter);
        Assert.Equal(2, tab.SelectedCount);
        Assert.Equal(beforeCursor, tab.Cursor);
        Assert.Contains("a.txt", tab.VisibleEntries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task BackToADeletedFolderKeepsLocationAndHistory()
    {
        using var dir = new TempFolder();
        var sub = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        var stop = TestContext.Current.CancellationToken;
        var tab = await Open(sub);
        Assert.True(await tab.GoAsync(new Location(dir.Path), showHidden: false, stop));
        Directory.Delete(sub);

        await tab.BackAsync(showHidden: false, stop);

        Assert.Equal(dir.Path, tab.Location.Path);
        Assert.NotNull(tab.Error);
        Assert.Contains("a.txt", tab.VisibleEntries.Select(entry => entry.Name));
        Directory.CreateDirectory(sub);
        await tab.BackAsync(showHidden: false, stop);
        Assert.Equal(sub, tab.Location.Path);
    }

    [Fact]
    public async Task CancelledNavigationKeepsTheCurrentListing()
    {
        using var dir = new TempFolder();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        var tab = await Open(dir.Path);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.False(await tab.GoAsync(new Location(Path.Combine(dir.Path, "sub")), showHidden: false, cancelled.Token));

        Assert.Equal(dir.Path, tab.Location.Path);
        Assert.Contains("a.txt", tab.VisibleEntries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task SelectionFilterAndTargets()
    {
        using var dir = new TempFolder();
        foreach (var name in new[] { "a.txt", "b.txt", "c.rs" })
        {
            File.WriteAllText(Path.Combine(dir.Path, name), "x");
        }

        var tab = await Open(dir.Path);
        tab.SelectMask("*.txt", select: true, includeDirectories: false);
        Assert.Equal(2, tab.Targets().Count);
        tab.InvertSelection();
        Assert.Equal("c.rs", tab.Targets()[0].Name);
        tab.ClearSelection();
        tab.Filter = "b";
        tab.ApplyFilter();
        Assert.Equal(1, tab.VisibleEntries.Count(entry => !entry.IsParent));
        tab.Filter = null;
        tab.ApplyFilter();
        tab.TypeAhead("c");
        Assert.Equal("c.rs", tab.Current?.Name);
        Assert.Equal("c.rs", tab.Targets()[0].Name);
    }

    [Fact]
    public async Task MouseSelectsAndUnselects()
    {
        using var dir = new TempFolder();
        Directory.CreateDirectory(Path.Combine(dir.Path, "d"));
        foreach (var name in new[] { "a.txt", "b.txt" })
        {
            File.WriteAllText(Path.Combine(dir.Path, name), "x");
        }

        var tab = await Open(dir.Path);
        Assert.Equal(new[] { "..", "d", "a.txt", "b.txt" }, tab.VisibleEntries.Select(entry => entry.Name).ToList());
        var folder = At(tab, "d");
        var a = At(tab, "a.txt");
        var b = At(tab, "b.txt");
        tab.Click(folder, ctrl: true, shift: false);
        tab.Click(a, ctrl: true, shift: false);
        Assert.Equal(new[] { "a.txt", "d" }, SortedTargets(tab));
        tab.Click(folder, ctrl: true, shift: false);
        Assert.Equal(new[] { "a.txt" }, SortedTargets(tab));
        tab.Click(b, ctrl: true, shift: false);
        tab.Point(a);
        Assert.Equal(new[] { "a.txt", "b.txt" }, SortedTargets(tab));
        tab.Point(folder);
        Assert.Equal(0, tab.SelectedCount);
        tab.Click(b, ctrl: false, shift: true);
        Assert.Equal(b - folder + 1, tab.SelectedCount);
        tab.Click(a, ctrl: false, shift: false);
        Assert.Equal(0, tab.SelectedCount);
        Assert.Equal(a, tab.Cursor);
    }

    [Fact]
    public async Task NewTabCloseAndNext()
    {
        using var dir = new TempFolder();
        var other = Path.Combine(dir.Path, "other");
        Directory.CreateDirectory(other);
        var stop = TestContext.Current.CancellationToken;
        var panel = await Panel.OpenAsync(
            [dir.Path, other],
            active: 0,
            SortOrder.NameAscending,
            showHidden: false,
            new LocalFileSystemProvider(),
            stop);
        Assert.Equal(2, panel.Tabs.Count);
        Assert.Equal(dir.Path, panel.Active.Location.Path);
        await panel.NewTabAsync(showHidden: false, stop);
        Assert.Equal(3, panel.Tabs.Count);
        Assert.Equal(1, panel.ActiveTab);
        Assert.Equal(dir.Path, panel.Active.Location.Path);
        panel.NextTab();
        Assert.Equal(other, panel.Active.Location.Path);
        panel.CloseTab();
        Assert.Equal(2, panel.Tabs.Count);
        panel.CloseTab();
        Assert.Single(panel.Tabs);
        Assert.Equal(dir.Path, panel.Active.Location.Path);
        panel.CloseTab();
        Assert.Single(panel.Tabs);
    }

    private static async Task<Tab> Open(string directory)
    {
        var tab = new Tab(new Location(directory), SortOrder.NameAscending, new LocalFileSystemProvider());
        await tab.ReloadAsync(showHidden: false, TestContext.Current.CancellationToken);
        Assert.Null(tab.Error);
        return tab;
    }

    private static int At(Tab tab, string name)
    {
        var index = 0;
        foreach (var entry in tab.VisibleEntries)
        {
            if (entry.Name == name)
            {
                return index;
            }

            index++;
        }

        throw new InvalidOperationException("Missing row " + name);
    }

    private static List<string> SortedTargets(Tab tab)
    {
        var names = tab.Targets().Select(entry => entry.Name).ToList();
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = Directory.CreateTempSubdirectory("d3sk-panel-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
