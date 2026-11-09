using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Tests.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class ComputerNavigationTests
{
    [Fact]
    public async Task UpFromAMountReachesComputerWithTheCursorOnIt()
    {
        var stop = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var mount = Directory.CreateDirectory(Path.Combine(temp.Path, "usb")).FullName;
        var sources = new StubComputerSources
        {
            Drives = [new DriveEntry(mount, "USB", "ext4", 1, 2)],
            Mounts = [mount],
        };
        var tab = new Tab(new Location(mount), SortOrder.NameAscending, PanelProviderFixture.Build(sources, [temp.Path]));
        await tab.ReloadAsync(showHidden: false, stop);

        var up = tab.ParentLocation();
        Assert.NotNull(up);
        Assert.Equal(ComputerAddress.Text, up.Path);
        Assert.True(await tab.GoAsync(up, showHidden: false, stop));
        Assert.Equal(ComputerAddress.Text, tab.Location.Path);
        Assert.Equal(mount, tab.Current?.Path);
        Assert.DoesNotContain(tab.VisibleEntries, entry => entry.IsParent);

        Assert.Null(tab.ParentLocation());

        await tab.BackAsync(showHidden: false, stop);
        Assert.Equal(mount, tab.Location.Path);
        await tab.ForwardAsync(showHidden: false, stop);
        Assert.Equal(ComputerAddress.Text, tab.Location.Path);
    }

    [Fact]
    public async Task UpFromARootReachesComputer()
    {
        var stop = TestContext.Current.CancellationToken;
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var tab = new Tab(new Location(root), SortOrder.NameAscending, PanelProviderFixture.Build(new StubComputerSources(), null));
        await tab.ReloadAsync(showHidden: false, stop);
        Assert.Equal(ComputerAddress.Text, tab.ParentLocation()?.Path);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("d3sk-computernav-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
