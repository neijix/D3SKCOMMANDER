using D3skCommander.Core.Commands;

namespace D3skCommander.Core.Panels;

/// <summary>The host operating system, as far as the context menu rule cares.</summary>
public enum MenuPlatform
{
    Windows,
    Linux,
    MacOS,
}

/// <summary>What one slot of a context menu is.</summary>
public enum MenuEntryKind
{
    Command,
    Separator,

    /// <summary>The Open-with submenu (association programs, then the Open-with dialog command).</summary>
    OpenWithSubmenu,
}

/// <summary>One slot of a context menu; <see cref="Command"/> is set for <see cref="MenuEntryKind.Command"/> only.</summary>
public readonly record struct MenuEntry(MenuEntryKind Kind, CommandId? Command)
{
    public static MenuEntry Of(CommandId command) => new(MenuEntryKind.Command, command);

    public static MenuEntry Rule { get; } = new(MenuEntryKind.Separator, null);

    public static MenuEntry OpenWithMenu { get; } = new(MenuEntryKind.OpenWithSubmenu, null);
}

/// <summary>
/// What a panel context menu holds for a target: the ordered entries, with every entry that cannot
/// apply hidden and no leading, trailing or doubled rule. Pure: no I/O.
/// </summary>
public static class PanelContextMenu
{
    /// <param name="target">What was right-clicked, the provider and the marked paths.</param>
    /// <param name="platform">The host OS: admin entries and the Windows menu are Windows only.</param>
    /// <param name="clipboardHasFiles">False hides Paste.</param>
    /// <param name="pwshFound">PowerShell on Linux needs <c>pwsh</c> on PATH; never offered on macOS.</param>
    public static IReadOnlyList<MenuEntry> Build(
        PanelContextTarget target,
        MenuPlatform platform,
        bool clipboardHasFiles,
        bool pwshFound)
    {
        var groups = target.Kind == ContextTargetKind.EmptySpace
            ? EmptySpaceGroups(target, platform, clipboardHasFiles, pwshFound)
            : ItemGroups(target, platform, clipboardHasFiles, pwshFound);

        var result = new List<MenuEntry>();
        foreach (var group in groups.Where(g => g.Count > 0))
        {
            if (result.Count > 0)
            {
                result.Add(MenuEntry.Rule);
            }

            result.AddRange(group);
        }

        return result;
    }

    private static List<List<MenuEntry>> ItemGroups(
        PanelContextTarget target, MenuPlatform platform, bool clipboardHasFiles, bool pwshFound)
    {
        var single = target.MarkedPaths.Count < 2;
        var isFolder = target.Kind == ContextTargetKind.Folder;
        var open = new List<MenuEntry>();
        if (isFolder)
        {
            open.Add(MenuEntry.Of(CommandId.Open));
            open.Add(MenuEntry.Of(CommandId.OpenInNewTab));
            open.Add(MenuEntry.Of(CommandId.OpenInOtherPanel));
        }
        else
        {
            open.Add(MenuEntry.Of(CommandId.Open));
            if (single)
            {
                open.Add(MenuEntry.OpenWithMenu);
            }

            open.Add(MenuEntry.Of(CommandId.View));
            open.Add(MenuEntry.Of(CommandId.Edit));
        }

        var clipboard = new List<MenuEntry> { MenuEntry.Of(CommandId.ClipCut), MenuEntry.Of(CommandId.ClipCopy) };
        // A Computer row is read-only: pasting "into" a drive row would write into the drive root.
        if (clipboardHasFiles && !(isFolder && ComputerAddress.IsAddress(target.PanelFolder)))
        {
            clipboard.Add(MenuEntry.Of(CommandId.ClipPaste));
        }

        clipboard.Add(MenuEntry.Of(CommandId.CopyNames));
        clipboard.Add(MenuEntry.Of(CommandId.CopyPaths));

        var edit = new List<MenuEntry>();
        if (single)
        {
            edit.Add(MenuEntry.Of(CommandId.RenameInPlace));
        }

        edit.Add(MenuEntry.Of(CommandId.Delete));
        if (!isFolder)
        {
            edit.Add(MenuEntry.Of(CommandId.DeletePermanent));
        }

        var archive = new List<MenuEntry> { MenuEntry.Of(CommandId.Pack) };
        if (target.Kind == ContextTargetKind.ArchiveFile)
        {
            archive.Add(MenuEntry.Of(CommandId.Unpack));
        }

        if (isFolder)
        {
            archive.Add(MenuEntry.Of(CommandId.DirSizes));
        }
        else
        {
            archive.Add(MenuEntry.Of(CommandId.ChecksumCreate));
        }

        var goTo = new List<MenuEntry>();
        if (target.Provider == PanelProviderKind.FlatList)
        {
            goTo.Add(MenuEntry.Of(CommandId.GoToContainingFolder));
        }

        var local = IsLocalPath(target.Provider);
        var favorites = local ? [MenuEntry.Of(CommandId.AddToFavorites)] : new List<MenuEntry>();
        var windows = local && platform == MenuPlatform.Windows && target.MarkedInOneFolder
            ? [MenuEntry.Of(CommandId.WindowsMenu)]
            : new List<MenuEntry>();

        return
        [
            open, clipboard, edit, archive, goTo, favorites,
            Terminals(target, platform, pwshFound), windows, [MenuEntry.Of(CommandId.Properties)],
        ];
    }

    private static List<List<MenuEntry>> EmptySpaceGroups(
        PanelContextTarget target, MenuPlatform platform, bool clipboardHasFiles, bool pwshFound)
    {
        var refresh = MenuEntry.Of(CommandId.Refresh);
        if (target.Provider == PanelProviderKind.FlatList)
        {
            return [[refresh]];
        }

        var create = new List<MenuEntry>();
        if (clipboardHasFiles)
        {
            create.Add(MenuEntry.Of(CommandId.ClipPaste));
        }

        create.Add(MenuEntry.Of(CommandId.MkDir));
        if (target.Provider != PanelProviderKind.Remote)
        {
            create.Add(MenuEntry.Of(CommandId.EditNew));
        }

        create.Add(refresh);

        var favorites = IsLocalPath(target.Provider) ? [MenuEntry.Of(CommandId.AddToFavorites)] : new List<MenuEntry>();
        var properties = target.Provider == PanelProviderKind.Remote
            ? new List<MenuEntry>()
            : [MenuEntry.Of(CommandId.Properties)];
        return [create, favorites, Terminals(target, platform, pwshFound), properties];
    }

    // Favorites, terminals and the Windows menu work on local paths only.
    private static bool IsLocalPath(PanelProviderKind provider) =>
        provider is PanelProviderKind.Local or PanelProviderKind.FlatList;

    private static List<MenuEntry> Terminals(PanelContextTarget target, MenuPlatform platform, bool pwshFound)
    {
        var list = new List<MenuEntry>();
        if (!IsLocalPath(target.Provider))
        {
            return list;
        }

        list.Add(MenuEntry.Of(CommandId.TerminalHere));
        if (platform == MenuPlatform.Windows)
        {
            list.Add(MenuEntry.Of(CommandId.TerminalAdmin));
        }

        if (platform == MenuPlatform.Windows || (platform == MenuPlatform.Linux && pwshFound))
        {
            list.Add(MenuEntry.Of(CommandId.PowerShellHere));
        }

        if (platform == MenuPlatform.Windows)
        {
            list.Add(MenuEntry.Of(CommandId.PowerShellAdmin));
        }

        return list;
    }
}
