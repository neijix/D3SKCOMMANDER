using D3skCommander.Core.Commands;

namespace D3skCommander.Core.Toolbar;

/// <summary>
/// The buttons of the column between the two panels: what works on the selection toward the other
/// panel (view, edit, copy, move, pack, new folder, delete), then the two-panel comparisons.
/// </summary>
public static class MiddleBarDefaults
{
    public static IReadOnlyList<ToolbarItem> Items { get; } =
    [
        Button(CommandId.View),
        Button(CommandId.Edit),
        ToolbarItem.Separator,
        Button(CommandId.Copy),
        Button(CommandId.Move),
        Button(CommandId.Pack),
        Button(CommandId.Unpack),
        Button(CommandId.MkDir),
        Button(CommandId.Delete),
        ToolbarItem.Separator,
        Button(CommandId.ComparePanels),
        Button(CommandId.CompareFiles),
        Button(CommandId.Sync),
    ];

    private static ToolbarItem Button(CommandId command) =>
        ToolbarItem.ForCommand(command, ToolbarIcons.ForCommand(command));
}
