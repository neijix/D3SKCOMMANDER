using D3skCommander.Core.Commands;

namespace D3skCommander.Core.Toolbar;

/// <summary>
/// The names of the toolbar's built-in vector icons. The App draws each one (<c>ToolbarGlyphs</c>);
/// a name here without a drawing fails a test.
/// </summary>
public static class ToolbarIcons
{
    public const string Generic = "command";

    public const string UserCommand = "bolt";

    public const string Program = "program";

    public static IReadOnlyList<string> Names { get; } =
    [
        "refresh",
        "copy",
        "move",
        "new_folder",
        "delete",
        "search",
        "quick_find",
        "disk_usage",
        "compare",
        "diff",
        "sync",
        "settings",
        "view",
        "edit",
        "parent",
        "home",
        "star",
        "terminal",
        "pack",
        "unpack",
        "properties",
        "drive",
        "bolt",
        "program",
        "command",
    ];

    private static readonly Dictionary<CommandId, string> ByCommand = new()
    {
        [CommandId.Refresh] = "refresh",
        [CommandId.Copy] = "copy",
        [CommandId.ClipCopy] = "copy",
        [CommandId.Move] = "move",
        [CommandId.MkDir] = "new_folder",
        [CommandId.Delete] = "delete",
        [CommandId.DeletePermanent] = "delete",
        [CommandId.Search] = "search",
        [CommandId.QuickFind] = "quick_find",
        [CommandId.DiskUsage] = "disk_usage",
        [CommandId.DirSizes] = "disk_usage",
        [CommandId.CompareFiles] = "diff",
        [CommandId.ComparePanels] = "compare",
        [CommandId.Sync] = "sync",
        [CommandId.Settings] = "settings",
        [CommandId.View] = "view",
        [CommandId.QuickView] = "view",
        [CommandId.Edit] = "edit",
        [CommandId.EditNew] = "edit",
        [CommandId.Parent] = "parent",
        [CommandId.Home] = "home",
        [CommandId.Hotlist] = "star",
        [CommandId.AddHotlist] = "star",
        [CommandId.Terminal] = "terminal",
        [CommandId.Pack] = "pack",
        [CommandId.Unpack] = "unpack",
        [CommandId.Properties] = "properties",
        [CommandId.DrivesLeft] = "drive",
        [CommandId.DrivesRight] = "drive",
    };

    public static bool IsKnown(string? name) =>
        !string.IsNullOrEmpty(name) && Names.Contains(name, StringComparer.Ordinal);

    /// <summary>The icon a command gets when the user picks none; the generic one for a command without its own.</summary>
    public static string ForCommand(CommandId command) =>
        ByCommand.TryGetValue(command, out var name) ? name : Generic;
}
