using D3skCommander.Core.Ftp;
using Xunit;

namespace D3skCommander.Core.Tests.Ftp;

public class FtpPathsTests
{
    [Theory]
    [InlineData("/", "a", "/a")]
    [InlineData("/pub", "a", "/pub/a")]
    [InlineData("/pub/", "a", "/pub/a")]
    [InlineData("", "a", "a")]
    public void JoinPutsAFolderAndANameTogether(string folder, string name, string expected) =>
        Assert.Equal(expected, FtpPaths.Join(folder, name));

    [Theory]
    [InlineData("/a/b", "/a", "b")]
    [InlineData("/a", "/", "a")]
    [InlineData("/a/b/", "/a", "b")]
    [InlineData("a", "", "a")]
    [InlineData("/", "/", "/")]
    public void ParentAndNameSplitAPath(string path, string parent, string name)
    {
        Assert.Equal(parent, FtpPaths.Parent(path));
        Assert.Equal(name, FtpPaths.Name(path));
    }

    [Fact]
    public void FoldersSortFirstThenByNameIgnoringCase()
    {
        var entries = new List<RemoteEntry>
        {
            new("b.txt", false, 1, null, ""),
            new("Zeta", true, 0, null, ""),
            new("A.txt", false, 1, null, ""),
            new("alpha", true, 0, null, ""),
        };
        FtpPaths.Sort(entries);
        Assert.Equal(["alpha", "Zeta", "A.txt", "b.txt"], entries.Select(e => e.Name).ToArray());
    }

    [Theory]
    [InlineData("file.txt", true)]
    [InlineData("a b", true)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("", false)]
    [InlineData("../x", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a\0b", false)]
    public void UnsafeRemoteNamesAreRefused(string name, bool safe) =>
        Assert.Equal(safe, FtpPaths.IsSafeName(name));
}
