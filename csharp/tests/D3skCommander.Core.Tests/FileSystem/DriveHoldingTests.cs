using D3skCommander.Core.FileSystem;
using Xunit;

namespace D3skCommander.Core.Tests.FileSystem;

public class DriveHoldingTests
{
    private static DriveEntry Drive(string path) => new(path, path, "", 0, 0);

    [Fact]
    public void TheLongestMountPrefixWins()
    {
        var drives = new[] { Drive("/"), Drive("/media/a"), Drive("/media") };

        Assert.Equal("/media/a", DriveList.Holding(drives, "/media/a/photos", windows: false)?.Path);
        Assert.Equal("/media", DriveList.Holding(drives, "/media/other", windows: false)?.Path);
        Assert.Equal("/", DriveList.Holding(drives, "/home/me", windows: false)?.Path);
    }

    [Fact]
    public void OnlyWholeSegmentsMatchAndTrailingSeparatorsAreTolerated()
    {
        var drives = new[] { Drive("/"), Drive("/media/a/") };

        Assert.Equal("/", DriveList.Holding(drives, "/media/ab", windows: false)?.Path);
        Assert.Equal("/media/a/", DriveList.Holding(drives, "/media/a", windows: false)?.Path);
        Assert.Equal("/media/a/", DriveList.Holding(drives, "/media/a/", windows: false)?.Path);
    }

    [Fact]
    public void UnixPathsCareAboutCase()
    {
        var drives = new[] { Drive("/Media/A") };

        Assert.Null(DriveList.Holding(drives, "/media/a/x", windows: false));
    }

    [Fact]
    public void WindowsPathsIgnoreCaseAndTakeBothSeparators()
    {
        var drives = new[] { Drive(@"C:\"), Drive(@"D:\") };

        Assert.Equal(@"D:\", DriveList.Holding(drives, @"d:\Games\x", windows: true)?.Path);
        Assert.Equal(@"C:\", DriveList.Holding(drives, "c:/Users/me", windows: true)?.Path);
        Assert.Equal(@"C:\", DriveList.Holding(drives, "C:", windows: true)?.Path);
        Assert.Null(DriveList.Holding(drives, @"E:\x", windows: true));
    }

    [Fact]
    public void NoMatchIsNull()
    {
        Assert.Null(DriveList.Holding([Drive("/media/a")], "/home", windows: false));
        Assert.Null(DriveList.Holding([], "/home", windows: false));
        Assert.Null(DriveList.Holding([Drive("/")], "ftp://host/x", windows: false));
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/media/usb stick", "usb stick")]
    [InlineData("/Volumes/Data/", "Data")]
    [InlineData(@"C:\", "C:")]
    [InlineData(@"\\server\share", "share")]
    [InlineData("/run/user/1000/gvfs/smb-share:server=nas.local,share=downloads", "downloads")]
    [InlineData("/run/user/1000/gvfs/sftp:host=build-box,user=me", "build-box")]
    [InlineData("/media/a-very-long-volume-label-here", "a-very-long-vol…")]
    public void ShortNames(string path, string expected) => Assert.Equal(expected, DriveList.ShortName(path));
}
