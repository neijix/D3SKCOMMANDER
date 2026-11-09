using D3skCommander.Core.Commands;
using D3skCommander.Core.Panels;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class PanelContextMenuTests
{
    private const string Rule = "-";
    private const string OpenWithMenu = "OpenWith*";

    private static PanelContextTarget Target(
        ContextTargetKind kind, PanelProviderKind provider = PanelProviderKind.Local, params string[] marked) =>
        new(kind, provider, "/docs", kind == ContextTargetKind.EmptySpace ? null : "/docs/x", marked);

    private static string Show(
        PanelContextTarget target,
        MenuPlatform platform = MenuPlatform.Linux,
        bool clipboard = true,
        bool pwsh = false) =>
        string.Join(
            ' ',
            PanelContextMenu.Build(target, platform, clipboard, pwsh).Select(e => e.Kind switch
            {
                MenuEntryKind.Separator => Rule,
                MenuEntryKind.OpenWithSubmenu => OpenWithMenu,
                _ => e.Command!.Value.ToString(),
            }));

    [Fact]
    public void FileOnLinuxWithoutPwsh() =>
        Assert.Equal(
            "Open OpenWith* View Edit - ClipCut ClipCopy ClipPaste CopyNames CopyPaths - RenameInPlace Delete DeletePermanent"
            + " - Pack ChecksumCreate - AddToFavorites - TerminalHere - Properties",
            Show(Target(ContextTargetKind.File)));

    [Fact]
    public void ArchiveFileAddsUnpack() =>
        Assert.Contains("Pack Unpack ChecksumCreate", Show(Target(ContextTargetKind.ArchiveFile)));

    [Fact]
    public void FileOnWindowsHasAdminEntriesAndWindowsMenu() =>
        Assert.EndsWith(
            "- AddToFavorites - TerminalHere TerminalAdmin PowerShellHere PowerShellAdmin - WindowsMenu - Properties",
            Show(Target(ContextTargetKind.File), MenuPlatform.Windows));

    [Theory]
    [InlineData(MenuPlatform.Linux, true, true)]
    [InlineData(MenuPlatform.Linux, false, false)]
    [InlineData(MenuPlatform.MacOS, true, false)]
    [InlineData(MenuPlatform.Windows, false, true)]
    public void PowerShellRule(MenuPlatform platform, bool pwsh, bool expected) =>
        Assert.Equal(expected, Show(Target(ContextTargetKind.File), platform, pwsh: pwsh).Contains("PowerShellHere"));

    [Fact]
    public void FolderEntries() =>
        Assert.Equal(
            "Open OpenInNewTab OpenInOtherPanel - ClipCut ClipCopy ClipPaste CopyNames CopyPaths - RenameInPlace Delete"
            + " - Pack DirSizes - AddToFavorites - TerminalHere - Properties",
            Show(Target(ContextTargetKind.Folder)));

    [Fact]
    public void NoPasteWithEmptyClipboard() =>
        Assert.DoesNotContain("ClipPaste", Show(Target(ContextTargetKind.File), clipboard: false));

    [Fact]
    public void ThreeMarksHaveNoRenameNorOpenWith()
    {
        var text = Show(Target(ContextTargetKind.File, PanelProviderKind.Local, "/docs/a", "/docs/b", "/docs/c"));
        Assert.DoesNotContain("RenameInPlace", text);
        Assert.DoesNotContain(OpenWithMenu, text);
        Assert.StartsWith("Open View Edit -", text);
    }

    [Fact]
    public void OneMarkKeepsRename() =>
        Assert.Contains("RenameInPlace", Show(Target(ContextTargetKind.File, PanelProviderKind.Local, "/docs/a")));

    [Fact]
    public void WindowsMenuHiddenForSetInSeveralFolders()
    {
        var target = Target(ContextTargetKind.File, PanelProviderKind.FlatList, "/a/x", "/b/y");
        Assert.DoesNotContain("WindowsMenu", Show(target, MenuPlatform.Windows));
        var same = Target(ContextTargetKind.File, PanelProviderKind.FlatList, "/a/x", "/a/y");
        Assert.Contains("WindowsMenu", Show(same, MenuPlatform.Windows));
    }

    [Fact]
    public void FlatListFileHasGoToContainingFolder() =>
        Assert.Contains(
            "- GoToContainingFolder -",
            Show(Target(ContextTargetKind.File, PanelProviderKind.FlatList)));

    [Fact]
    public void LocalFileHasNoGoToContainingFolder() =>
        Assert.DoesNotContain("GoToContainingFolder", Show(Target(ContextTargetKind.File)));

    [Theory]
    [InlineData(PanelProviderKind.Remote)]
    [InlineData(PanelProviderKind.Archive)]
    public void RemoteAndArchiveHideFavoritesTerminalsAndWindowsMenu(PanelProviderKind provider)
    {
        foreach (var kind in new[] { ContextTargetKind.File, ContextTargetKind.Folder, ContextTargetKind.EmptySpace })
        {
            var text = Show(Target(kind, provider), MenuPlatform.Windows);
            Assert.DoesNotContain("AddToFavorites", text);
            Assert.DoesNotContain("Terminal", text);
            Assert.DoesNotContain("PowerShell", text);
            Assert.DoesNotContain("WindowsMenu", text);
        }
    }

    [Fact]
    public void ComputerFolderRowHasNoPasteButARealRemoteFolderKeepsIt()
    {
        var computerRow = new PanelContextTarget(
            ContextTargetKind.Folder, PanelProviderKind.Remote, ComputerAddress.Text, "C:\\", ["C:\\"]);
        Assert.DoesNotContain("ClipPaste", Show(computerRow));
        Assert.Contains("ClipCopy", Show(computerRow));
        Assert.Contains("ClipPaste", Show(Target(ContextTargetKind.Folder, PanelProviderKind.Remote)));
    }

    [Fact]
    public void LocalEmptySpace() =>
        Assert.Equal(
            "ClipPaste MkDir EditNew Refresh - AddToFavorites - TerminalHere - Properties",
            Show(Target(ContextTargetKind.EmptySpace)));

    [Fact]
    public void EmptySpaceWithEmptyClipboardHasNoPaste() =>
        Assert.StartsWith("MkDir EditNew Refresh", Show(Target(ContextTargetKind.EmptySpace), clipboard: false));

    [Fact]
    public void FlatListEmptySpaceIsRefreshOnly() =>
        Assert.Equal("Refresh", Show(Target(ContextTargetKind.EmptySpace, PanelProviderKind.FlatList), MenuPlatform.Windows));

    [Fact]
    public void RemoteEmptySpaceHasNoNewFileNorProperties() =>
        Assert.Equal("ClipPaste MkDir Refresh", Show(Target(ContextTargetKind.EmptySpace, PanelProviderKind.Remote)));

    [Fact]
    public void ArchiveEmptySpaceKeepsPropertiesWithoutTerminals() =>
        Assert.Equal(
            "ClipPaste MkDir EditNew Refresh - Properties",
            Show(Target(ContextTargetKind.EmptySpace, PanelProviderKind.Archive)));

    [Theory]
    [InlineData(ContextTargetKind.File, PanelProviderKind.Local, MenuPlatform.Linux)]
    [InlineData(ContextTargetKind.Folder, PanelProviderKind.Archive, MenuPlatform.Windows)]
    [InlineData(ContextTargetKind.EmptySpace, PanelProviderKind.Remote, MenuPlatform.MacOS)]
    [InlineData(ContextTargetKind.File, PanelProviderKind.Remote, MenuPlatform.Windows)]
    public void NeverALeadingTrailingOrDoubledRule(ContextTargetKind kind, PanelProviderKind provider, MenuPlatform platform)
    {
        var entries = PanelContextMenu.Build(Target(kind, provider), platform, false, false);
        Assert.NotEqual(MenuEntryKind.Separator, entries[0].Kind);
        Assert.NotEqual(MenuEntryKind.Separator, entries[^1].Kind);
        for (var i = 1; i < entries.Count; i++)
        {
            Assert.False(entries[i].Kind == MenuEntryKind.Separator && entries[i - 1].Kind == MenuEntryKind.Separator);
        }
    }
}
