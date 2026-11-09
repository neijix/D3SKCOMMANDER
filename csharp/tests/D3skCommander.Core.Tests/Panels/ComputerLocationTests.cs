using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class ComputerLocationTests : IDisposable
{
    private static readonly string[] s_prefixes = ["/media", "/run/media", "/mnt", "/Volumes"];

    private readonly string _root = Directory.CreateTempSubdirectory("d3sk-computer-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("computer:", true)]
    [InlineData("COMPUTER:", true)]
    [InlineData("  Computer:  ", true)]
    [InlineData("computer", false)]
    [InlineData("computer:x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TheAddressIsRecognizedInAnyCaseIgnoringBlanks(string? text, bool expected)
    {
        Assert.Equal(expected, ComputerAddress.IsAddress(text));
    }

    [Theory]
    [InlineData("computer:", true)]
    [InlineData("Computer:/x", true)]
    [InlineData("computer:\\x", true)]
    [InlineData("computer:x", false)]
    [InlineData("C:\\computer:", false)]
    public void AnAddressOrAPathUnderItIsRecognized(string text, bool expected)
    {
        Assert.Equal(expected, ComputerAddress.IsAddressOrUnder(text));
    }

    [Fact]
    public void TheCanonicalTextIsLowerCase()
    {
        Assert.Equal("computer:", ComputerAddress.Text);
    }

    [Theory]
    [InlineData("/", false, true)]
    [InlineData("C:\\", true, true)]
    [InlineData("c:", true, true)]
    [InlineData("c:/", true, true)]
    [InlineData("\\\\server\\share", true, true)]
    [InlineData("\\\\server\\share\\", true, true)]
    [InlineData("//server/share", true, true)]
    [InlineData("\\\\server\\share\\sub", true, false)]
    [InlineData("\\\\server", true, false)]
    [InlineData("C:\\Users", true, false)]
    [InlineData("C:\\", false, false)]
    [InlineData("/home", false, false)]
    public void PathRootsGoUpToComputer(string path, bool windows, bool expected)
    {
        Assert.Equal(expected, ComputerAddress.GoesUp(path, [], s_prefixes, windows));
    }

    [Theory]
    [InlineData("/media/me/USB", true)]
    [InlineData("/run/media/me/USB", true)]
    [InlineData("/mnt/data", true)]
    [InlineData("/Volumes/Backup", true)]
    [InlineData("/Volumes/Backup/", true)]
    [InlineData("/media/me", false)]
    [InlineData("/home", false)]
    [InlineData("/boot", false)]
    [InlineData("/media/me/USB/sub", false)]
    [InlineData("/mntx/data", false)]
    [InlineData("/media", false)]
    public void AMountPointUnderAPrefixGoesUpToComputer(string path, bool expected)
    {
        string[] mounts = ["/", "/home", "/boot", "/media", "/media/me/USB", "/run/media/me/USB", "/mnt/data", "/mntx/data", "/Volumes/Backup"];
        Assert.Equal(expected, ComputerAddress.GoesUp(path, mounts, s_prefixes, false));
    }

    [Fact]
    public void AFolderUnderAPrefixThatIsNotAMountPointGoesUpNormally()
    {
        Assert.False(ComputerAddress.GoesUp("/mnt/data", ["/"], s_prefixes, false));
    }

    [Fact]
    public void OnWindowsCaseAndSeparatorsDoNotMatter()
    {
        Assert.True(ComputerAddress.GoesUp("d:\\Mount\\usb", ["D:/mount/USB"], ["D:\\MOUNT"], true));
        Assert.False(ComputerAddress.GoesUp("d:\\Mount\\usb", ["D:/mount/USB"], ["D:\\MOUNT"], false));
    }

    [Fact]
    public void ARootLocationGoesUpToTheAddressAndTheAddressToNothing()
    {
        var root = Path.GetPathRoot(_root)!;
        Assert.Equal(ComputerAddress.Text, new Location(root).Parent()?.Path);
        Assert.Null(new Location(ComputerAddress.Text).Parent());
        Assert.Null(new Location("COMPUTER:").Parent());
    }

    [Fact]
    public void AFolderAnArchivePathAndAListKeepTheirParent()
    {
        var folder = Path.Combine(_root, "sub");
        Assert.Equal(_root, new Location(folder).Parent()?.Path);
        var zip = Path.Combine(_root, "a.zip");
        Assert.Equal(_root, new Location(zip).Parent()?.Path);
    }

    [Fact]
    public void ALocationWithItsOwnProviderAtTheRootKeepsTodaysParent()
    {
        var root = Path.GetPathRoot(_root)!;
        Assert.Null(new Location(root, new LocalFileSystemProvider()).Parent());
    }

    [Fact]
    public async Task TheTabFollowsTheDotDotRowOfTheListing()
    {
        var tab = new Tab(new Location(_root), SortOrder.NameAscending, new UpToComputerProvider());
        await tab.ReloadAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(ComputerAddress.Text, tab.ParentLocation()?.Path);
        var up = tab.VisibleEntries.First(entry => entry.IsParent);
        Assert.Equal(ComputerAddress.Text, tab.EnterLocation(up)?.Path);
    }

    [Fact]
    public async Task TheTabFallsBackToTheLocationParentWithoutADotDotRow()
    {
        var tab = new Tab(new Location(_root), SortOrder.NameAscending, new LocalFileSystemProvider());
        await tab.ReloadAsync(false, TestContext.Current.CancellationToken);
        tab.SetFilter("nothing-matches-this");

        Assert.Equal(Path.GetDirectoryName(_root), tab.ParentLocation()?.Path);
    }

    [Fact]
    public async Task BranchViewAtComputerIsRefusedAndTheTabListsNormally()
    {
        var tab = new Tab(new Location(ComputerAddress.Text), SortOrder.NameAscending, new UpToComputerProvider());
        await tab.ReloadAsync(false, TestContext.Current.CancellationToken);

        await tab.SetBranchAsync(true, false, TestContext.Current.CancellationToken);

        Assert.False(tab.Branch);
        Assert.Null(tab.Error);
        Assert.NotEmpty(tab.VisibleEntries);
    }

    /// <summary>Lists one ".." row to the address and one folder, whatever it is asked.</summary>
    private sealed class UpToComputerProvider : IFileSystemProvider
    {
        public bool IsReadOnly => true;

        public async IAsyncEnumerable<FileResult<Entry>> ListAsync(
            string directory,
            bool showHidden,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return FileResult<Entry>.Ok(new Entry("..", ComputerAddress.Text, true, false, true, 0, null, false, false, ""));
            yield return FileResult<Entry>.Ok(new Entry("one", Path.Combine(directory, "one"), true, false, false, 0, null, false, false, ""));
        }

        public Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
