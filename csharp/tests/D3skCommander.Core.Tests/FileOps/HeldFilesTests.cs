using D3skCommander.Core.FileOps;
using Xunit;

namespace D3skCommander.Core.Tests.FileOps;

public class HeldFilesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "d3sk-held");

    [Fact]
    public void TheFileItselfOrAFolderAboveItCoversIt()
    {
        var clip = Path.Combine(Root, "films", "clip.mp4");
        Assert.True(HeldFiles.Covers([clip], clip));
        Assert.True(HeldFiles.Covers([Path.Combine(Root, "other.txt"), Path.Combine(Root, "films")], clip));
        Assert.True(HeldFiles.Covers([Root + Path.DirectorySeparatorChar], clip));
    }

    [Fact]
    public void ASiblingOrANameThatOnlyStartsTheSameDoesNot()
    {
        var clip = Path.Combine(Root, "films", "clip.mp4");
        Assert.False(HeldFiles.Covers([Path.Combine(Root, "films", "clip.mp4.txt")], clip));
        Assert.False(HeldFiles.Covers([Path.Combine(Root, "film")], clip));
        Assert.False(HeldFiles.Covers([], clip));
        Assert.False(HeldFiles.Covers([clip], null));
    }
}
