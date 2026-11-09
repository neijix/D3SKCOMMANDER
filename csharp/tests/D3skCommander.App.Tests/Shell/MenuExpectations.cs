using D3skCommander.Core.Commands;

namespace D3skCommander.App.Tests.Shell;

/// <summary>v0.1 menu order, encoded so the lists are not a second copy of <c>MenuLayout</c>.</summary>
internal static class MenuExpectations
{
    public static IReadOnlyList<string> Titles { get; } =
    [
        "menu.files",
        "menu.mark",
        "menu.commands",
        "menu.net",
        "menu.show",
        "menu.tools",
        "menu.config",
        "menu.help",
    ];

    public static CommandId?[] Items(string titleKey) => Parse(Spec(titleKey));

    public static CommandId[] AddOns() =>
        Names("Split Combine ChecksumCreate ChecksumVerify AddTime ReDate Touch Catalog Encrypt Decrypt BackupZip Ftp NetConnect NetDisconnect BrowseFolder Sync");

    private static string Spec(string titleKey)
    {
        if (titleKey == "menu.files")
        {
            return "Open OpenInNewTab OpenInOtherPanel | View Edit EditNew Copy Move ClipCopy ClipCut ClipPaste RenameInPlace MkDir Delete DeletePermanent | Pack Unpack BackupZip | Properties OpenWith | Quit";
        }

        if (titleKey == "menu.mark")
        {
            return "SelectGroup UnselectGroup SelectAll UnselectAll Invert SelectSameExt SelectAllButFirst | CopyNames CopyPaths | ComparePanels";
        }

        if (titleKey == "menu.commands")
        {
            return "Search QuickFind MultiRename CompareFiles Sync | DirSizes DiskUsage Branch QuickFilter | BrowseFolder Hotlist AddHotlist AddToFavorites History GoToContainingFolder | Back Forward Parent Root Home | SameDir Swap Terminal TerminalHere TerminalAdmin PowerShellHere PowerShellAdmin";
        }

        if (titleKey == "menu.net")
        {
            return "NetConnect NetDisconnect | Ftp Passwords";
        }

        if (titleKey == "menu.show")
        {
            return "ViewFull ViewBrief ViewThumbs QuickView ToggleHidden ToggleDriveBar ShowToolbar ToggleTree ToggleTreeLeft ToggleTreeRight | SortName SortExt SortDate SortSize | NextColumnSet | NewTab CloseTab NextTab | Refresh";
        }

        if (titleKey == "menu.tools")
        {
            return "Split Combine ChecksumCreate ChecksumVerify | AddTime ReDate Touch | Catalog Encrypt Decrypt";
        }

        if (titleKey == "menu.config")
        {
            return "Settings EditColumnSets CustomizeToolbar ManageFavoriteTabs EditFileAssociations SettingsFolder Log";
        }

        if (titleKey == "menu.help")
        {
            return "Keys CheckUpdate | About";
        }

        throw new ArgumentException($"Unknown menu '{titleKey}'.", nameof(titleKey));
    }

    private static CommandId?[] Parse(string spec)
    {
        var parts = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var items = new CommandId?[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            items[i] = parts[i] == "|" ? null : Enum.Parse<CommandId>(parts[i]);
        }

        return items;
    }

    private static CommandId[] Names(string spec)
    {
        var parts = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var items = new CommandId[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            items[i] = Enum.Parse<CommandId>(parts[i]);
        }

        return items;
    }
}
