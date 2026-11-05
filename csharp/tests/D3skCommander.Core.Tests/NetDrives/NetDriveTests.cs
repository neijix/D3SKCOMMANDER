using D3skCommander.Core.NetDrives;
using Xunit;

namespace D3skCommander.Core.Tests.NetDrives;

public class NetDriveTests
{
    [Fact]
    public void ParseReadsUncSlashAndUrlForms()
    {
        var media = ShareOf("smb", null, "nas", "media", "");
        Assert.Equal(media, Parsed(@"\\nas\media"));
        Assert.Equal(media, Parsed(@"\\nas\media\"));
        Assert.Equal(media, Parsed("//nas/media"));
        Assert.Equal(media, Parsed("nas/media"));
        Assert.Equal(media, Parsed(" smb://nas/media/ "));
        Assert.Equal(media, Parsed("cifs://nas/media"));
        Assert.Equal(ShareOf("smb", null, "nas", "media", "films/2024"), Parsed(@"\\nas\media\films\2024"));
        Assert.Equal(
            ShareOf("smb", "bob", "nas.example.com", "media", "x"),
            Parsed("SMB://bob@nas.example.com/media/x"));
        Assert.Equal(
            ShareOf("sftp", "bob", "host.example.com", "", ""),
            Parsed("sftp://bob@host.example.com"));
    }

    [Fact]
    public void ParseRefusesWhatCannotBeMounted()
    {
        Assert.False(NetDrive.Parse("").IsSuccess);
        Assert.False(NetDrive.Parse(@"\\").IsSuccess);
        Assert.False(NetDrive.Parse(@"\\nas").IsSuccess);
        Assert.False(NetDrive.Parse("smb://@/media").IsSuccess);
        Assert.False(NetDrive.Parse("http://example.com/x").IsSuccess);
    }

    [Fact]
    public void ShareGivesUriUncAndInnerFolder()
    {
        var share = Parsed(@"\\nas\media\films\2024");
        Assert.Equal("smb://nas/media/", share.Uri());
        Assert.Equal(@"\\nas\media", share.Unc());
        var mount = Path.Combine(Path.DirectorySeparatorChar.ToString(), "mnt", "media");
        Assert.Equal(Path.Combine(mount, "films", "2024"), share.Inside(mount));
        Assert.Equal("sftp://bob@h/", Parsed("sftp://bob@h").Uri());
    }

    [Fact]
    public void ApplescriptStringsEscapeQuotesAndBackslashes()
    {
        Assert.Equal("\"a\\\"b\\\\c\"", NetDrive.AppleScriptString("a\"b\\c"));
    }

    [Fact]
    public void MacScriptPutsDomainUserInTheUrlAndQuotesThePassword()
    {
        var share = Parsed(@"\\nas\media");
        var credentials = new Credentials("bob", "CORP", "p\"w");
        Assert.Equal(
            "mount volume \"smb://CORP;bob@nas/media/\" with password \"p\\\"w\"",
            NetDrive.MacMountScript(share, credentials));
        Assert.Equal(
            "mount volume \"smb://nas/media/\"",
            NetDrive.MacMountScript(share, Credentials.None));
        Assert.Equal(
            "mount volume \"https://h/files/\"",
            NetDrive.MacMountScript(Parsed("davs://h/files"), Credentials.None));
    }

    [Fact]
    public void GioAnswersAreUserDomainPasswordLines()
    {
        var credentials = new Credentials("bob", "", "secret");
        Assert.Equal("bob\n\nsecret\n", NetDrive.GioAnswers(credentials));
    }

    [Fact]
    public void GioInfoGivesTheLocalFolderOfAMountedShare()
    {
        var mounted = Fixture("gio/2.88/info-smb-mounted.txt");
        Assert.Equal(
            "/run/user/1000/gvfs/smb-share:server=nas.example.com,share=downloads",
            NetDrive.GioLocalPath(mounted));
        Assert.Null(NetDrive.GioLocalPath(Fixture("gio/2.88/info-not-mounted.txt")));
    }

    [Fact]
    public void GvfsFoldersMapBackToTheirUri()
    {
        const string root = "/run/user/1000/gvfs";
        var smb = root + "/smb-share:server=nas.example.com,share=down%2cloads";
        Assert.Equal("smb://nas.example.com/down,loads/", NetDrive.GvfsUri(smb + "/sub/dir"));
        Assert.Equal(
            "sftp://bob@h.example.com:2222/",
            NetDrive.GvfsUri(root + "/sftp:host=h.example.com,port=2222,user=bob"));
        Assert.Equal(
            "davs://d.example.com/",
            NetDrive.GvfsUri(root + "/dav:host=d.example.com,ssl=true"));
        Assert.Null(NetDrive.GvfsUri("/home/bob/gvfs-notes"));
        Assert.Null(NetDrive.GvfsUri("/media/usb"));
    }

    [Fact]
    public void MacMountOutputFindsTheShareAndTheMountOfAPath()
    {
        var output = Fixture("macos-mount/15/mount.txt");
        var mounts = NetDrive.MacNetworkMounts(output);
        Assert.Equal(3, mounts.Count);
        Assert.Equal(
            "/Volumes/media",
            NetDrive.MacMountPoint(output, Parsed(@"\\nas.example.com\media")));
        Assert.Equal(
            "/Volumes/Public Share",
            NetDrive.MacMountPoint(output, Parsed(@"\\NAS\public share")));
        Assert.Null(NetDrive.MacMountPoint(output, Parsed(@"\\other\media")));
        Assert.Equal(
            "/Volumes/Public Share",
            NetDrive.MacMountContaining(output, "/Volumes/Public Share/docs/a.txt"));
        Assert.Null(NetDrive.MacMountContaining(output, "/Volumes/USB KEY/x"));
    }

    [Fact]
    public void WindowsRootsAndDriveLetters()
    {
        Assert.Equal(@"\\nas\media", NetDrive.WindowsRoot(@"\\nas\media\films"));
        Assert.Equal(@"\\nas\media", NetDrive.WindowsRoot("//nas/media"));
        Assert.Equal("Z:", NetDrive.WindowsRoot(@"z:\films"));
        Assert.Null(NetDrive.WindowsRoot(@"\\?\C:\x"));
        Assert.Null(NetDrive.WindowsRoot("/home/bob"));
        Assert.Null(NetDrive.DriveLetter("").Value.Letter);
        Assert.Equal("Z:", NetDrive.DriveLetter("z").Value.Letter);
        Assert.Equal("Z:", NetDrive.DriveLetter("Z:\\").Value.Letter);
        Assert.False(NetDrive.DriveLetter("ZZ").IsSuccess);
        Assert.False(NetDrive.DriveLetter("1").IsSuccess);
    }

    [Fact]
    public void RemembersRecentAddresses()
    {
        var first = NetDrive.Remember([], " \\\\nas\\media ");
        Assert.Equal([@"\\nas\media"], first);
        var second = NetDrive.Remember(first, "smb://other/share");
        Assert.Equal(["smb://other/share", @"\\nas\media"], second);
        var again = NetDrive.Remember(second, @"\\nas\media");
        Assert.Equal([@"\\nas\media", "smb://other/share"], again);

        var many = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            many.Add("s" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var capped = NetDrive.Remember(many, "new");
        Assert.Equal(NetDrive.RecentLimit, capped.Count);
        Assert.Equal("new", capped[0]);
        Assert.Equal("s8", capped[9]);
        Assert.DoesNotContain(capped, address => address.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void PasswordStaysOffTheCommandLine()
    {
        var share = Parsed(@"\\nas\media");
        var credentials = new Credentials("bob", "CORP", "s3cret-value");
        var command = NetDrive.MountCommandFor(share, credentials);
        Assert.DoesNotContain("s3cret-value", command.Program, StringComparison.Ordinal);
        Assert.DoesNotContain(command.Arguments, argument => argument.Contains("s3cret-value", StringComparison.Ordinal));
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("WNetAddConnection2", command.Program);
            Assert.Empty(command.Arguments);
            Assert.DoesNotContain("s3cret-value", command.StandardInput, StringComparison.Ordinal);
            return;
        }

        Assert.Contains("s3cret-value", command.StandardInput, StringComparison.Ordinal);
    }

    [Fact]
    public void ListsConnectedShares()
    {
        var mounts = NetDrive.ConnectedFromMountOutput(Fixture("macos-mount/15/mount.txt"));
        Assert.Equal(3, mounts.Count);
        Assert.Contains(mounts, share => share.Path == "/Volumes/media");

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("d3sk-net-").FullName;
        try
        {
            var gvfs = Path.Combine(root, "gvfs");
            Directory.CreateDirectory(Path.Combine(gvfs, "smb-share:server=nas,share=media"));
            Directory.CreateDirectory(Path.Combine(gvfs, "junk"));
            var shares = NetDrive.ConnectedFromGvfs(gvfs);
            Assert.Equal(["smb://nas/media"], shares.Select(share => share.Label).ToArray());
            Assert.Empty(NetDrive.ConnectedFromGvfs(Path.Combine(root, "missing")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Manual check against a real server. Runs only when <c>D3SK_TEST_SHARE</c> is set
    /// (<c>D3SK_TEST_USER</c>, <c>D3SK_TEST_DOMAIN</c>, <c>D3SK_TEST_PASSWORD</c> optional).
    /// Leaves the share connected.
    /// </summary>
    [Fact]
    public void ConnectsToARealShare()
    {
        var address = Environment.GetEnvironmentVariable("D3SK_TEST_SHARE");
        if (address is null)
        {
            Assert.Skip("D3SK_TEST_SHARE is not set");
        }

        var credentials = new Credentials(
            Environment.GetEnvironmentVariable("D3SK_TEST_USER") ?? "",
            Environment.GetEnvironmentVariable("D3SK_TEST_DOMAIN") ?? "",
            Environment.GetEnvironmentVariable("D3SK_TEST_PASSWORD") ?? "");
        var connected = NetDrive.Connect(Parsed(address), credentials, null);
        Assert.True(connected.IsSuccess, connected.Error?.Message);
        Assert.True(Directory.Exists(connected.Value), connected.Value + " is not a folder");
        _ = Directory.EnumerateFileSystemEntries(connected.Value).ToArray();
    }

    private static Share ShareOf(string scheme, string? user, string server, string name, string sub) =>
        new(scheme, user, server, name, sub);

    private static Share Parsed(string input)
    {
        var parsed = NetDrive.Parse(input);
        Assert.True(parsed.IsSuccess, parsed.Error?.Message);
        return parsed.Value;
    }

    private static string Fixture(string relative)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", relative.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }
}
