using D3skCommander.Core.Commands;

namespace D3skCommander.App.Tests.Shell;

/// <summary>Commands the shell can run today. Everything else stays visible and disabled.</summary>
internal static class HandledCommands
{
    private static readonly HashSet<CommandId> Enabled =
    [
        CommandId.Quit,
        CommandId.Copy,
        CommandId.Move,
        CommandId.MkDir,
        CommandId.Delete,
        CommandId.SelectAll,
        CommandId.UnselectAll,
        CommandId.Invert,
        CommandId.BrowseFolder,
        CommandId.Back,
        CommandId.Forward,
        CommandId.Parent,
        CommandId.Root,
        CommandId.Home,
        CommandId.SameDir,
        CommandId.Swap,
        CommandId.ViewFull,
        CommandId.ViewBrief,
        CommandId.ToggleHidden,
        CommandId.SortName,
        CommandId.SortExt,
        CommandId.SortDate,
        CommandId.SortSize,
        CommandId.NewTab,
        CommandId.CloseTab,
        CommandId.NextTab,
        CommandId.Refresh,
        CommandId.SwitchPanel,
    ];

    public static bool Handles(CommandId command) => Enabled.Contains(command);
}
