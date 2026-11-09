using D3skCommander.Core.Panels;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class PanelContextTargetTests
{
    private static PanelContextTarget Target(ContextTargetKind kind, string? entry, params string[] marked) =>
        new(kind, PanelProviderKind.Local, "/docs", entry, marked);

    [Theory]
    [InlineData(ContextTargetKind.File, "/docs/sub/a.txt", "/docs/sub")]
    [InlineData(ContextTargetKind.ArchiveFile, "/docs/b.zip", "/docs")]
    [InlineData(ContextTargetKind.File, "/a.txt", "/")]
    [InlineData(ContextTargetKind.File, @"C:\Docs\report.pdf", @"C:\Docs")]
    [InlineData(ContextTargetKind.File, @"C:\report.pdf", @"C:\")]
    [InlineData(ContextTargetKind.Folder, "/docs/sub", "/docs/sub")]
    [InlineData(ContextTargetKind.EmptySpace, null, "/docs")]
    public void TheHereFolderFollowsTheTarget(ContextTargetKind kind, string? entry, string expected)
    {
        Assert.Equal(expected, Target(kind, entry).HereFolder);
    }

    [Fact]
    public void TheParentRowUsesThePanelFolder()
    {
        Assert.Equal("/docs", Target(ContextTargetKind.EmptySpace, "/").HereFolder);
    }

    [Fact]
    public void NoMarksOrOneMarkSitInOneFolder()
    {
        Assert.True(Target(ContextTargetKind.File, "/docs/a").MarkedInOneFolder);
        Assert.True(Target(ContextTargetKind.File, "/docs/a", "/docs/a").MarkedInOneFolder);
    }

    [Fact]
    public void MarksFromTheSameFolderSitInOneFolder()
    {
        Assert.True(Target(ContextTargetKind.File, "/docs/a", "/docs/a", "/docs/b").MarkedInOneFolder);
    }

    [Fact]
    public void MarksFromTwoFoldersDoNot()
    {
        Assert.False(Target(ContextTargetKind.File, "/docs/a", "/docs/a", "/other/b").MarkedInOneFolder);
    }

    [Fact]
    public void ProviderKindIsKept()
    {
        var target = new PanelContextTarget(ContextTargetKind.File, PanelProviderKind.FlatList, "/s", "/x/a", []);

        Assert.Equal(PanelProviderKind.FlatList, target.Provider);
    }
}
