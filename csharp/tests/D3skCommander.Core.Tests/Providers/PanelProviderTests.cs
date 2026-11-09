using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.Providers;

public class PanelProviderTests
{
    [Fact]
    public async Task ARootGainsOneParentRowToComputer()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var provider = PanelProviderFixture.Build(new StubComputerSources(), null);
        var rows = await List(provider, root);
        var parents = rows.Where(row => row.IsParent).ToList();
        Assert.Single(parents);
        Assert.Equal(ComputerAddress.Text, parents[0].Path);
        Assert.Equal("..", parents[0].Name);
        Assert.True(parents[0].IsDirectory);
        Assert.True(rows[0].IsParent);
    }

    [Fact]
    public async Task AnEmptyRootStillGetsOneParentRowToComputer()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var sources = new StubComputerSources();
        var text = Localizer.Load(Language.En, Language.En);
        var provider = new PanelFileSystemProvider(new EmptyProvider(), new ComputerFileSystemProvider(sources, text), sources);
        var rows = await List(provider, root);
        var parent = Assert.Single(rows);
        Assert.True(parent.IsParent);
        Assert.Equal(ComputerAddress.Text, parent.Path);
    }

    [Fact]
    public async Task AMountUnderAPrefixHasItsLocalParentRowReplaced()
    {
        using var temp = new TempDir();
        var mount = Directory.CreateDirectory(Path.Combine(temp.Path, "usb")).FullName;
        File.WriteAllText(Path.Combine(mount, "a.txt"), "x");
        var provider = PanelProviderFixture.Build(new StubComputerSources { Mounts = [mount] }, [temp.Path]);
        var rows = await List(provider, mount);
        var parents = rows.Where(row => row.IsParent).ToList();
        Assert.Single(parents);
        Assert.Equal(ComputerAddress.Text, parents[0].Path);
        Assert.True(rows[0].IsParent);
        Assert.Contains(rows, row => row.Name == "a.txt");
    }

    [Fact]
    public async Task AMountOutsideThePrefixesKeepsItsLocalParentRow()
    {
        using var temp = new TempDir();
        var mount = Directory.CreateDirectory(Path.Combine(temp.Path, "home")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(temp.Path, "other")).FullName;
        var provider = PanelProviderFixture.Build(new StubComputerSources { Mounts = [mount] }, [other]);
        var rows = await List(provider, mount);
        var parent = Assert.Single(rows, row => row.IsParent);
        Assert.Equal(temp.Path, parent.Path);
    }

    [Fact]
    public async Task AFolderThatIsNotAMountKeepsItsLocalParentRow()
    {
        using var temp = new TempDir();
        var folder = Directory.CreateDirectory(Path.Combine(temp.Path, "plain")).FullName;
        var provider = PanelProviderFixture.Build(new StubComputerSources { Mounts = [] }, [temp.Path]);
        var parent = Assert.Single(await List(provider, folder), row => row.IsParent);
        Assert.Equal(temp.Path, parent.Path);
    }

    [Fact]
    public async Task OrdinaryListingsReadNoOsMountState()
    {
        using var temp = new TempDir();
        var folder = Directory.CreateDirectory(Path.Combine(temp.Path, "plain")).FullName;
        var sources = new StubComputerSources();
        var provider = PanelProviderFixture.Build(sources, ["/media", "/mnt"]);
        await List(provider, folder);
        Assert.Equal(0, sources.MountReads);
    }

    [Fact]
    public async Task AMissingFolderIsStillNotFoundWithNoParentRow()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var missing = Path.Combine(root, "d3sk-no-such-folder-" + Guid.NewGuid().ToString("N"));
        var provider = PanelProviderFixture.Build(new StubComputerSources(), null);
        var items = new List<FileResult<Entry>>();
        await foreach (var item in provider.ListAsync(missing, false, CancellationToken.None))
        {
            items.Add(item);
        }

        var only = Assert.Single(items);
        Assert.Equal(ErrorKind.NotFound, only.Error?.Kind);
    }

    [Fact]
    public async Task TheAddressGoesToTheComputerProvider()
    {
        using var temp = new TempDir();
        var provider = PanelProviderFixture.Build(
            new StubComputerSources { Drives = [new DriveEntry(temp.Path, "Lab", "ext4", 1, 2)] },
            null);
        var rows = await List(provider, " Computer: ");
        Assert.Contains(rows, row => row.Path == temp.Path);
        Assert.DoesNotContain(rows, row => row.IsParent);
        var self = await provider.GetEntryAsync(ComputerAddress.Text, CancellationToken.None);
        Assert.True(self.IsSuccess, self.Error?.Message);
        Assert.Equal(ComputerAddress.Text, self.Value.Path);
    }

    [Fact]
    public async Task UnderTheAddressAReadIsNotFound()
    {
        var provider = PanelProviderFixture.Build(new StubComputerSources(), null);
        var path = ComputerAddress.Text + "/x";
        var items = new List<FileResult<Entry>>();
        await foreach (var item in provider.ListAsync(path, false, CancellationToken.None))
        {
            items.Add(item);
        }

        Assert.Equal(ErrorKind.NotFound, Assert.Single(items).Error?.Kind);
        var entry = await provider.GetEntryAsync(path, CancellationToken.None);
        Assert.Equal(ErrorKind.NotFound, entry.Error?.Kind);
    }

    [Fact]
    public async Task WritesAtOrUnderTheAddressAreRefused()
    {
        var provider = PanelProviderFixture.Build(new StubComputerSources(), null);
        foreach (var path in new[] { ComputerAddress.Text, ComputerAddress.Text + "/new", @"COMPUTER:\new" })
        {
            AssertReadOnly((await provider.CreateFolderAsync(path, CancellationToken.None)).Error, path);
            AssertReadOnly((await provider.OpenWriteAsync(path, CancellationToken.None)).Error, path);
            AssertReadOnly((await provider.RenameAsync(path, "x", CancellationToken.None)).Error, path);
            AssertReadOnly((await provider.DeleteAsync(path, CancellationToken.None)).Error, path);
        }
    }

    [Fact]
    public async Task OtherPathsWriteLikeLocal()
    {
        using var temp = new TempDir();
        var provider = PanelProviderFixture.Build(new StubComputerSources(), null);
        var made = Path.Combine(temp.Path, "made");
        var created = await provider.CreateFolderAsync(made, CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.True(Directory.Exists(made));
        Assert.False(provider.IsReadOnly);
    }

    private static void AssertReadOnly(OperationError? error, string path)
    {
        Assert.NotNull(error);
        Assert.Equal(ErrorKind.Unsupported, error.Kind);
        Assert.Equal(FileSystemProvider.ReadOnlyMessage, error.Message);
        Assert.Equal(path, error.Path);
    }

    private static async Task<List<Entry>> List(IFileSystemProvider provider, string directory)
    {
        var rows = new List<Entry>();
        await foreach (var item in provider.ListAsync(directory, false, CancellationToken.None))
        {
            Assert.True(item.IsSuccess, item.Error?.Message);
            rows.Add(item.Value);
        }

        return rows;
    }

    // A local provider whose every listing succeeds with no rows.
    private sealed class EmptyProvider : IFileSystemProvider
    {
        public bool IsReadOnly => false;

        public async IAsyncEnumerable<FileResult<Entry>> ListAsync(
            string directory,
            bool showHidden,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("d3sk-panelprov-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
