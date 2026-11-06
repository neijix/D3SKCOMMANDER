using D3skCommander.Core.FileSystem;
using Xunit;

namespace D3skCommander.Core.Tests.FileSystem;

public class UdisksAndPlacesTests
{
    [Theory]
    [InlineData("Mounted /dev/sdb1 at /media/user/USB.\n", "/media/user/USB")]
    [InlineData("Mounted /dev/sdb1 at /media/user/My Stick.", "/media/user/My Stick")]
    [InlineData("Mounted /dev/sdb1 at /run/media/u/KEY32", "/run/media/u/KEY32")]
    public void TheMountFolderIsRead(string stdout, string expected) =>
        Assert.Equal(expected, Udisks.ParseMounted(stdout));

    [Theory]
    [InlineData("")]
    [InlineData("Error mounting /dev/sdb1: busy")]
    [InlineData("Mounted /dev/sdb1")]
    [InlineData("Mounted /dev/sdb1 at relative.")]
    public void GarbageGivesNull(string stdout) => Assert.Null(Udisks.ParseMounted(stdout));

    [Fact]
    public void PlacesFollowTheUserDirsFileAndSkipMissingFolders()
    {
        const string userDirs = "# comment\nXDG_DESKTOP_DIR=\"$HOME\"\nXDG_MUSIC_DIR=\"$HOME/Tunes\"\nXDG_DOWNLOAD_DIR=\"/data/dl\"\n";
        var present = new HashSet<string> { "/h", "/h/Tunes", "/data/dl", "/h/Documents", "/h/Music" };

        var places = UserPlaces.Standard("/h", userDirs, present.Contains);

        Assert.Equal(
            [("places.home", "/h"), ("places.documents", "/h/Documents"), ("places.downloads", "/data/dl"), ("places.music", "/h/Tunes")],
            places.Select(p => (p.Key, p.Path)));
    }

    [Fact]
    public void WithoutAUserDirsFileThePlainNamesAreUsed()
    {
        var places = UserPlaces.Standard("/h", null, path => path is "/h" or "/h/Downloads");

        Assert.Equal(["/h", "/h/Downloads"], places.Select(p => p.Path));
    }
}
