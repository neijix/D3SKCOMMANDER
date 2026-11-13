using System.Runtime.CompilerServices;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Tests.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.NetDrives;

/// <summary>A Windows server root, <c>\\server\</c>: the list of its shares, a favorite target, a server login.</summary>
public class ServerRootTests
{
    [Fact]
    public void UncPathTellsServerRootsFromShareRoots()
    {
        Assert.Equal("192.168.1.15", UncPath.Server("\\\\192.168.1.15\\"));
        Assert.Equal("nas", UncPath.Server("\\\\nas"));
        Assert.Equal("nas", UncPath.Server("//nas/"));
        Assert.Null(UncPath.Server("\\\\nas\\media"));
        Assert.Null(UncPath.Server("\\\\?\\"));
        Assert.Null(UncPath.Server("\\\\.\\"));
        Assert.Null(UncPath.Server("C:\\"));
        Assert.Null(UncPath.Server("/"));
        Assert.Null(UncPath.Server("\\\\\\nas"));
        Assert.Null(UncPath.Server(null));

        Assert.Equal("nas", UncPath.ShareServer("\\\\nas\\media\\"));
        Assert.Null(UncPath.ShareServer("\\\\nas\\media\\films"));
        Assert.Null(UncPath.ShareServer("\\\\nas"));

        Assert.Equal("\\\\nas\\", UncPath.ServerRoot("nas"));
        Assert.Equal("\\\\nas\\media", UncPath.ShareRoot("nas", "media"));
    }

    [Fact]
    public async Task TheServerProviderListsTheSharesAfterARowToHome()
    {
        var provider = new ServerFileSystemProvider(new FixedShares("media", "backup"));

        var rows = await List(provider, "\\\\nas\\");

        Assert.Equal(["..", "media", "backup"], rows.Select(row => row.Name));
        Assert.Equal([ComputerAddress.Text, "\\\\nas\\media", "\\\\nas\\backup"], rows.Select(row => row.Path));
        Assert.True(rows[0].IsParent);
        Assert.All(rows, row => Assert.True(row.IsDirectory));
        Assert.True(provider.IsReadOnly);
        var created = await provider.CreateFolderAsync("\\\\nas\\x", CancellationToken.None);
        Assert.Equal(ErrorKind.Unsupported, created.Error?.Kind);
    }

    [Fact]
    public async Task AServerThatRefusesTheListingGivesItsError()
    {
        var denied = OperationError.AccessDenied("Access is denied.", "\\\\nas\\");
        var provider = new ServerFileSystemProvider(new FixedShares { Failure = denied });

        var items = new List<FileResult<Entry>>();
        await foreach (var item in provider.ListAsync("\\\\nas\\", false, CancellationToken.None))
        {
            items.Add(item);
        }

        var only = Assert.Single(items);
        Assert.Equal(ErrorKind.AccessDenied, only.Error?.Kind);
    }

    [Fact]
    public async Task OnWindowsAServerRootListsItsSharesAndAShareRootGoesUpToItsServer()
    {
        var sources = new StubComputerSources();
        var text = Localizer.Load(Language.En, Language.En);
        var provider = new PanelFileSystemProvider(
            new OneFileProvider(),
            new ComputerFileSystemProvider(sources, text),
            sources,
            windows: true,
            servers: new ServerFileSystemProvider(new FixedShares("media")));

        var server = await List(provider, "\\\\nas\\");
        Assert.Equal([ComputerAddress.Text, "\\\\nas\\media"], server.Select(row => row.Path));

        var share = await List(provider, "\\\\nas\\media");
        Assert.Equal(["..", "a.txt"], share.Select(row => row.Name));
        Assert.Equal("\\\\nas\\", share[0].Path);
        Assert.True(share[0].IsParent);
    }

    [Fact]
    public async Task APanelReopensAtAServerRootOnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "server roots are a Windows feature");
        var sources = new StubComputerSources();
        var provider = new PanelFileSystemProvider(
            new OneFileProvider(),
            new ComputerFileSystemProvider(sources, Localizer.Load(Language.En, Language.En)),
            sources,
            windows: true,
            servers: new ServerFileSystemProvider(new FixedShares("media")));

        var panel = await Panel.OpenAsync(["\\\\nas\\"], 0, SortOrder.NameAscending, false, provider, CancellationToken.None);

        Assert.Equal("\\\\nas\\", panel.Active.Location.Path);
        Assert.Null(panel.Active.Error);
    }

    [Fact]
    public void AServerRootTakesNoWriteButCanBeCopiedFrom()
    {
        Assert.True(FileOpGuard.IsTargetRefused("\\\\nas\\"));
        Assert.False(FileOpGuard.IsTargetRefused("\\\\nas\\media"));
        Assert.True(FileOpGuard.IsRefused(GuardedOp.Delete, "\\\\nas\\"));
        Assert.True(FileOpGuard.IsRefused(GuardedOp.NewFolder, "\\\\nas\\"));
        Assert.False(FileOpGuard.IsRefused(GuardedOp.Copy, "\\\\nas\\", "C:\\data"));
        Assert.False(FileOpGuard.IsRefused(GuardedOp.Delete, "\\\\nas\\media"));
    }

    [Fact]
    public void AShareRootGoesUpToItsServerOnWindows()
    {
        var parent = new Location("\\\\nas\\media").Parent();

        Assert.NotNull(parent);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("\\\\nas\\", parent.Path);
            Assert.Equal(ComputerAddress.Text, parent.Parent()?.Path);
        }
    }

    [Fact]
    public void AServerAloneParsesOnlyForAServerLogin()
    {
        Assert.False(NetDrive.Parse("\\\\192.168.1.15\\").IsSuccess);

        var login = NetDrive.Parse("\\\\192.168.1.15\\", serverOnly: true);

        Assert.True(login.IsSuccess, login.Error?.Message);
        Assert.Equal("192.168.1.15", login.Value.Server);
        Assert.Equal("", login.Value.Name);
        Assert.False(NetDrive.Parse("afp://nas", serverOnly: true).IsSuccess);
    }

    [Fact]
    public void AServerLoginIsListedAsTheServerRoot()
    {
        Assert.Equal(new ConnectedShare("\\\\nas", "\\\\nas\\"), NetDrive.ConnectedFromWnet("\\\\nas\\IPC$", null));
        Assert.Equal(new ConnectedShare("\\\\nas\\ipc$", "Z:"), NetDrive.ConnectedFromWnet("\\\\nas\\ipc$", "Z:"));
        Assert.Equal(new ConnectedShare("\\\\nas\\media", "\\\\nas\\media"), NetDrive.ConnectedFromWnet("\\\\nas\\media", null));
        Assert.Equal(new ConnectedShare("\\\\nas\\media", "Z:"), NetDrive.ConnectedFromWnet("\\\\nas\\media", "Z:"));
    }

    [Fact]
    public void OnlyPlainDiskSharesAreBrowsable()
    {
        Assert.True(NetDrive.IsBrowsableShare(0, "media"));
        Assert.False(NetDrive.IsBrowsableShare(0, "secret$"));
        Assert.False(NetDrive.IsBrowsableShare(0x80000000, "ADMIN"));
        Assert.False(NetDrive.IsBrowsableShare(1, "printer"));
        Assert.False(NetDrive.IsBrowsableShare(3, "IPC"));
    }

    [Fact]
    public void ALoginIsOnlySavedOnWindowsWithAUserName()
    {
        var saved = NetDrive.Remember("nas", new Credentials("", "", "pw"));

        Assert.False(saved.IsSuccess);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Contains("Windows", saved.Error?.Message, StringComparison.Ordinal);
        }
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

    private sealed class FixedShares(params string[] names) : IServerShares
    {
        public OperationError? Failure { get; init; }

        public FileResult<IReadOnlyList<string>> Shares(string server) =>
            Failure is null ? FileResult<IReadOnlyList<string>>.Ok(names) : FileResult<IReadOnlyList<string>>.Fail(Failure);
    }

    // A local provider whose every folder holds its own ".." row and one file.
    private sealed class OneFileProvider : IFileSystemProvider
    {
        public bool IsReadOnly => false;

        public async IAsyncEnumerable<FileResult<Entry>> ListAsync(
            string directory,
            bool showHidden,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return FileResult<Entry>.Ok(new Entry("..", directory + "\\..", true, false, true, 0, null, false, false, ""));
            yield return FileResult<Entry>.Ok(new Entry("a.txt", directory + "\\a.txt", false, false, false, 1, null, false, false, ""));
        }

        public Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
