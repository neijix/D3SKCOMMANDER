namespace D3skCommander.Core.Commands;

/// <summary>v0.1 <c>MENUS</c>, same order and separators. SwitchPanel, DrivesLeft and DrivesRight stay key-only.</summary>
internal static class MenuLayout
{
    public static IReadOnlyList<MenuDefinition> Menus { get; } =
    [
        Files(),
        Mark(),
        Commands(),
        Net(),
        Show(),
        Tools(),
        Config(),
        Help(),
    ];

    private static MenuDefinition Files() => Menu("menu.files",
    [
        CommandId.Open,
        CommandId.OpenInNewTab,
        CommandId.OpenInOtherPanel,
        null,
        CommandId.View,
        CommandId.Edit,
        CommandId.EditNew,
        CommandId.Copy,
        CommandId.Move,
        CommandId.ClipCopy,
        CommandId.ClipCut,
        CommandId.ClipPaste,
        CommandId.RenameInPlace,
        CommandId.MkDir,
        CommandId.Delete,
        CommandId.DeletePermanent,
        null,
        CommandId.Pack,
        CommandId.Unpack,
        CommandId.BackupZip,
        null,
        CommandId.Properties,
        CommandId.OpenWith,
        CommandId.WindowsMenu,
        null,
        CommandId.Quit,
    ]);

    private static MenuDefinition Mark() => Menu("menu.mark",
    [
        CommandId.SelectGroup,
        CommandId.UnselectGroup,
        CommandId.SelectAll,
        CommandId.UnselectAll,
        CommandId.Invert,
        CommandId.SelectSameExt,
        CommandId.SelectAllButFirst,
        null,
        CommandId.CopyNames,
        CommandId.CopyPaths,
        null,
        CommandId.ComparePanels,
    ]);

    private static MenuDefinition Commands() => Menu("menu.commands",
    [
        CommandId.Search,
        CommandId.QuickFind,
        CommandId.MultiRename,
        CommandId.CompareFiles,
        CommandId.Sync,
        null,
        CommandId.DirSizes,
        CommandId.DiskUsage,
        CommandId.Branch,
        CommandId.QuickFilter,
        null,
        CommandId.BrowseFolder,
        CommandId.Hotlist,
        CommandId.AddHotlist,
        CommandId.AddToFavorites,
        CommandId.History,
        CommandId.GoToContainingFolder,
        null,
        CommandId.Back,
        CommandId.Forward,
        CommandId.Parent,
        CommandId.Root,
        CommandId.Home,
        null,
        CommandId.SameDir,
        CommandId.Swap,
        CommandId.Terminal,
        CommandId.TerminalHere,
        CommandId.TerminalAdmin,
        CommandId.PowerShellHere,
        CommandId.PowerShellAdmin,
    ]);

    private static MenuDefinition Net() => Menu("menu.net",
    [
        CommandId.NetConnect,
        CommandId.NetDisconnect,
        null,
        CommandId.Ftp,
        CommandId.Passwords,
    ]);

    private static MenuDefinition Show() => Menu("menu.show",
    [
        CommandId.ViewFull,
        CommandId.ViewBrief,
        CommandId.ViewThumbs,
        CommandId.QuickView,
        CommandId.ToggleHidden,
        CommandId.ToggleDriveBar,
        CommandId.ShowToolbar,
        CommandId.ToggleTree,
        CommandId.ToggleTreeLeft,
        CommandId.ToggleTreeRight,
        null,
        CommandId.SortName,
        CommandId.SortExt,
        CommandId.SortDate,
        CommandId.SortSize,
        null,
        CommandId.NextColumnSet,
        null,
        CommandId.NewTab,
        CommandId.CloseTab,
        CommandId.NextTab,
        null,
        CommandId.Refresh,
    ]);

    private static MenuDefinition Tools() => Menu("menu.tools",
    [
        CommandId.Split,
        CommandId.Combine,
        CommandId.ChecksumCreate,
        CommandId.ChecksumVerify,
        null,
        CommandId.AddTime,
        CommandId.ReDate,
        CommandId.Touch,
        null,
        CommandId.Catalog,
        CommandId.Encrypt,
        CommandId.Decrypt,
    ]);

    private static MenuDefinition Config() => Menu("menu.config",
    [
        CommandId.Settings,
        CommandId.EditColumnSets,
        CommandId.CustomizeToolbar,
        CommandId.ManageFavoriteTabs,
        CommandId.EditFileAssociations,
        CommandId.SettingsFolder,
        CommandId.Log,
    ]);

    private static MenuDefinition Help() => Menu("menu.help",
    [
        CommandId.Keys,
        CommandId.CheckUpdate,
        null,
        CommandId.About,
    ]);

    private static MenuDefinition Menu(string titleKey, CommandId?[] items) => new(titleKey, items);
}
