using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class PanelSessionTests
{
    [Fact]
    public void NothingSavedOpensTheHomeFolder()
    {
        var start = PanelSession.Restore(null, "/home/me", _ => true);

        Assert.Equal(["/home/me"], start.Paths);
        Assert.Equal(0, start.Active);
        Assert.Equal(PanelViewMode.Full, start.View);
    }

    [Fact]
    public void ATabSavedAtComputerComesBackWhateverTheExistsCheckSays()
    {
        var saved = new SessionPanel
        {
            ActiveIndex = 1,
            Tabs = [new SessionTab { Directory = "/gone" }, new SessionTab { Directory = "Computer:" }],
        };

        var start = PanelSession.Restore(saved, "/home/me", _ => false);

        Assert.Equal(["Computer:"], start.Paths);
        Assert.Equal(0, start.Active);
    }

    [Fact]
    public void AMissingFolderIsDroppedAndTheActiveTabFollowsItsFolder()
    {
        var saved = new SessionPanel
        {
            ActiveIndex = 2,
            Tabs =
            [
                new SessionTab { Directory = "/a" },
                new SessionTab { Directory = "/gone" },
                new SessionTab { Directory = "/c", Sort = new SortOrder(SortKey.Size, true), ViewMode = PanelViewMode.Thumbnails },
            ],
        };

        var start = PanelSession.Restore(saved, "/home/me", path => path != "/gone");

        Assert.Equal(["/a", "/c"], start.Paths);
        Assert.Equal(1, start.Active);
        Assert.Equal(new SortOrder(SortKey.Size, true), start.Sort);
        Assert.Equal(PanelViewMode.Thumbnails, start.View);
    }

    [Fact]
    public void AMissingActiveFolderFallsBackToTheTabBeforeIt()
    {
        var saved = new SessionPanel
        {
            ActiveIndex = 1,
            Tabs = [new SessionTab { Directory = "/a", ViewMode = PanelViewMode.Brief }, new SessionTab { Directory = "/gone" }],
        };

        var start = PanelSession.Restore(saved, "/home/me", path => path != "/gone");

        Assert.Equal(["/a"], start.Paths);
        Assert.Equal(0, start.Active);
        Assert.Equal(PanelViewMode.Brief, start.View);
    }

    [Fact]
    public async Task CaptureKeepsEveryTabTheActiveOneAndTheView()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-session-").FullName;
        try
        {
            var other = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
            var panel = await Panel.OpenAsync(
                [root, other],
                1,
                SortOrder.NameAscending,
                showHidden: false,
                new LocalFileSystemProvider(),
                TestContext.Current.CancellationToken);

            var saved = PanelSession.Capture(panel, PanelViewMode.Brief);

            Assert.Equal(1, saved.ActiveIndex);
            Assert.Equal([root, other], saved.Tabs.Select(tab => tab.Directory).ToArray());
            Assert.All(saved.Tabs, tab => Assert.Equal(PanelViewMode.Brief, tab.ViewMode));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EachTabKeepsItsColumnSetThroughCaptureAndRestore()
    {
        var saved = new SessionPanel
        {
            ActiveIndex = 0,
            Tabs =
            [
                new SessionTab { Directory = "/a", ColumnSet = "Details" },
                new SessionTab { Directory = "/gone", ColumnSet = "Wide" },
                new SessionTab { Directory = "/c", ColumnSet = "Photos" },
            ],
        };

        var start = PanelSession.Restore(saved, "/home/me", path => path != "/gone");

        Assert.Equal(["/a", "/c"], start.Paths);
        Assert.Equal(["Details", "Photos"], start.ColumnSets);
    }

    [Fact]
    public void ATabWithoutASavedSetShowsDefault()
    {
        Assert.Equal(ColumnSetCatalog.DefaultName, new SessionTab().ColumnSet);
        Assert.Equal(ColumnSetCatalog.DefaultName, new SessionTab { ColumnSet = " " }.ColumnSet);
    }
}
