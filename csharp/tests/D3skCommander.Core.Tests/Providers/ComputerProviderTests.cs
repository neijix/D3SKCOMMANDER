using D3skCommander.Core.Common;
using D3skCommander.Core.FileKinds;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.Core.Tests.Providers;

public class ComputerProviderTests
{
    private static readonly Localizer Text = Localizer.Load(Language.En, Language.En);

    [Fact]
    public async Task GroupsListInOrderDrivesNetworkFavoritesWithoutPlaces()
    {
        using var temp = new TempDir();
        var sources = new FakeSources
        {
            Drives = [new DriveEntry("C:\\", "System", "NTFS", 5UL << 30, 100UL << 30), new DriveEntry("D:\\", "", "NTFS", 1, 2)],
            Shares = [new ConnectedShare("nas", @"\nas\share")],
            Place = [new UserPlace("places.home", temp.Path)],
            Favs = [new HotlistItem("proj", temp.Path)],
        };
        var rows = await List(Provider(sources, windows: true));
        Assert.Equal([1, 1, 2, 4], rows.Select(r => r.Group).ToArray());
        Assert.Equal([DriveKind.Local, DriveKind.Local, DriveKind.Network, DriveKind.None], rows.Select(r => r.Drive).ToArray());
        Assert.Equal(
            [FileKind.Drive, FileKind.Drive, FileKind.NetworkDrive, FileKind.Folder],
            rows.Select(FileKindClassifier.Of).ToArray());
        Assert.Equal("Network drive", ColumnValues.Kind(rows[2], Text));
        Assert.Equal(["C:\\", "D:\\", @"\nas\share", temp.Path], rows.Select(r => r.Path).ToArray());
        Assert.All(rows, r => Assert.True(r.IsDirectory && !r.IsParent));
        Assert.Equal("System (C:)", rows[0].Name);
        Assert.Equal("Local disk (D:)", rows[1].Name);
        Assert.Equal(100UL << 30, rows[0].Size);
        Assert.Equal(0.95, rows[0].UsedFraction!.Value, 3);
        Assert.Equal("100.0 GB", rows[0].SizeText);
        Assert.Equal("5.0 GB free of 100.0 GB", rows[0].FillText);
        Assert.Null(rows[2].UsedFraction);
        Assert.Equal("", rows[2].SizeText);
        Assert.Equal("proj", rows[3].Name);
        Assert.Null(rows[3].SizeText);
    }

    [Fact]
    public async Task ShareMatchingAMountedDriveIsListedOnceInTheNetworkGroup()
    {
        var sources = new FakeSources
        {
            Drives = [new DriveEntry("C:\\", "", "NTFS", 1, 2), new DriveEntry("Z:\\", "nas", "NTFS", 1, 7)],
            Shares = [new ConnectedShare("nas", "z:")],
        };
        var rows = await List(Provider(sources, windows: true));
        Assert.Equal(["C:\\", "Z:\\"], rows.Select(r => r.Path).ToArray());
        Assert.Equal([1, 2], rows.Select(r => r.Group).ToArray());
        Assert.Equal(7UL, rows[1].Size);
    }

    [Theory]
    [InlineData("cifs")]
    [InlineData("SMB3")]
    [InlineData("smbfs")]
    [InlineData("nfs")]
    [InlineData("nfs4")]
    [InlineData("afpfs")]
    [InlineData("webdav")]
    [InlineData("davfs")]
    [InlineData("fuse.sshfs")]
    public async Task NetworkFormatDriveGoesInTheNetworkGroup(string format)
    {
        var sources = new FakeSources { Drives = [new DriveEntry("/", "", "ext4", 1, 2), new DriveEntry("/mnt/nas", "", format, 1, 2)] };
        var provider = Provider(sources, windows: false);
        var rows = await List(provider);
        Assert.Equal([1, 2], rows.Select(r => r.Group).ToArray());
        Assert.Equal(["/"], provider.LocalDrivePaths());
    }

    [Fact]
    public async Task ThrowingSourceIsLeftOutAndTheRestStillList()
    {
        using var temp = new TempDir();
        var sources = new FakeSources { Favs = [new HotlistItem("proj", temp.Path)], ThrowDrives = true, ThrowShares = true };
        var rows = await List(Provider(sources, windows: true));
        Assert.Single(rows);
        Assert.Equal(4, rows[0].Group);
    }

    [Fact]
    public async Task FileAndMissingFavoritesAreLeftOut()
    {
        using var temp = new TempDir();
        var file = Path.Combine(temp.Path, "f.txt");
        File.WriteAllText(file, "x");
        var sources = new FakeSources
        {
            Favs = [new HotlistItem("file", file), new HotlistItem("gone", Path.Combine(temp.Path, "nope")), new HotlistItem("ok", temp.Path)],
        };
        var rows = await List(Provider(sources, windows: false));
        Assert.Equal(["ok"], rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public async Task EveryListingReadsTheSourcesAgain()
    {
        var sources = new FakeSources();
        var provider = Provider(sources, windows: false);
        await List(provider);
        await List(provider);
        Assert.Equal(2, sources.DriveReads);
    }

    [Fact]
    public async Task NoParentRowAndAddressInAnyCase()
    {
        var provider = Provider(new FakeSources(), windows: false);
        Assert.DoesNotContain(await List(provider, " Computer: "), r => r.IsParent);
        var entry = await provider.GetEntryAsync("COMPUTER:", CancellationToken.None);
        Assert.True(entry.IsSuccess);
        Assert.Equal(Text.Get("computer.title"), entry.Value.Name);
        Assert.True(entry.Value.IsDirectory);
    }

    [Fact]
    public async Task OtherPathsAreNotFoundAndReadingTheAddressIsInvalid()
    {
        var provider = Provider(new FakeSources(), windows: false);
        await foreach (var item in provider.ListAsync("/tmp", true, CancellationToken.None))
        {
            Assert.Equal(ErrorKind.NotFound, item.Error!.Kind);
        }

        Assert.Equal(ErrorKind.NotFound, (await provider.GetEntryAsync("/tmp", CancellationToken.None)).Error!.Kind);
        Assert.Equal(ErrorKind.InvalidData, (await provider.OpenReadAsync("computer:", CancellationToken.None)).Error!.Kind);
    }

    [Fact]
    public async Task EveryMutationIsRefusedAsUnsupported()
    {
        var provider = Provider(new FakeSources(), windows: false);
        Assert.True(provider.IsReadOnly);
        var errors = new[]
        {
            (await provider.OpenWriteAsync("computer:", CancellationToken.None)).Error,
            (await provider.CreateFolderAsync("computer:/x", CancellationToken.None)).Error,
            (await provider.RenameAsync("computer:", "x", CancellationToken.None)).Error,
            (await provider.DeleteAsync("computer:", CancellationToken.None)).Error,
        };
        Assert.All(errors, e =>
        {
            Assert.Equal(ErrorKind.Unsupported, e!.Kind);
            Assert.Equal(FileSystemProvider.ReadOnlyMessage, e.Message);
        });
    }

    [Fact]
    public async Task ScanTargetFollowsTheCursorRow()
    {
        using var temp = new TempDir();
        var sources = new FakeSources
        {
            Drives = [new DriveEntry("/", "", "ext4", 1, 2), new DriveEntry("/mnt/nas", "", "cifs", 1, 2)],
            Favs = [new HotlistItem("proj", temp.Path)],
        };
        var provider = Provider(sources, windows: false);
        var rows = await List(provider);
        Assert.Equal("/", provider.ScanTarget(rows[0]).Folder);
        Assert.Equal("/mnt/nas", provider.ScanTarget(rows[1]).Folder);
        var other = provider.ScanTarget(rows[2]);
        Assert.Null(other.Folder);
        Assert.Equal(["/"], other.Drives);
        Assert.Null(provider.ScanTarget(null).Folder);
    }

    [Theory]
    [InlineData("computer.title")]
    [InlineData("computer.readonly")]
    [InlineData("computer.menu")]
    public void TextsExistInBothLanguagesAndDiffer(string key)
    {
        var localizer = Localizer.Load(Language.En, Language.En);
        var en = localizer.Get(key);
        localizer.Use(Language.Fr);
        var fr = localizer.Get(key);
        Assert.NotEqual(key, en);
        Assert.NotEqual(key, fr);
        Assert.NotEqual(en, fr);
    }

    private static ComputerFileSystemProvider Provider(FakeSources sources, bool windows) => new(sources, Text, windows);

    private static async Task<List<Entry>> List(ComputerFileSystemProvider provider, string address = "computer:")
    {
        var rows = new List<Entry>();
        await foreach (var item in provider.ListAsync(address, false, CancellationToken.None))
        {
            Assert.True(item.IsSuccess, item.Error?.Message);
            rows.Add(item.Value);
        }

        return rows;
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("d3sk-computer-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FakeSources : IComputerSources
    {
        public IReadOnlyList<DriveEntry> Drives { get; init; } = [];

        public IReadOnlyList<ConnectedShare> Shares { get; init; } = [];

        public IReadOnlyList<UserPlace> Place { get; init; } = [];

        public IReadOnlyList<HotlistItem> Favs { get; init; } = [];

        public bool ThrowDrives { get; init; }

        public bool ThrowShares { get; init; }

        public int DriveReads { get; private set; }

        public IReadOnlyList<DriveEntry> MountedDrives()
        {
            DriveReads++;
            return ThrowDrives ? throw new IOException("drives") : Drives;
        }

        public IReadOnlyList<ConnectedShare> ConnectedShares() => ThrowShares ? throw new IOException("shares") : Shares;

        public IReadOnlyList<UserPlace> Places() => Place;

        public IReadOnlyList<HotlistItem> Favorites() => Favs;

        public IReadOnlyList<string> MountPoints() => [];
    }
}
