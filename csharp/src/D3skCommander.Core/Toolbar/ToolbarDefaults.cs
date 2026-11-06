using D3skCommander.Core.Commands;

namespace D3skCommander.Core.Toolbar;

/// <summary>The toolbar a new settings folder starts with, and what "reset" restores.</summary>
public static class ToolbarDefaults
{
    public static List<ToolbarItem> Items() =>
    [
        Button(CommandId.Refresh),
        ToolbarItem.Separator,
        Button(CommandId.Copy),
        Button(CommandId.Move),
        Button(CommandId.MkDir),
        Button(CommandId.Delete),
        ToolbarItem.Separator,
        Button(CommandId.Search),
        Button(CommandId.QuickFind),
        Button(CommandId.DiskUsage),
        ToolbarItem.Separator,
        Button(CommandId.CompareFiles),
        Button(CommandId.Sync),
        Button(CommandId.Settings),
    ];

    private static ToolbarItem Button(CommandId command) =>
        ToolbarItem.ForCommand(command, ToolbarIcons.ForCommand(command));
}
