namespace D3skCommander.Core.Commands;

/// <summary>
/// The icon buttons in front of each panel's path box, left to right (v0.1 <c>path_bar</c>, plus
/// network and FTP). The drive button opens the list of the panel it sits on.
/// </summary>
internal static class PanelBarLayout
{
    private static readonly CommandId[] Left = Build(CommandId.DrivesLeft);
    private static readonly CommandId[] Right = Build(CommandId.DrivesRight);

    public static IReadOnlyList<CommandId> For(bool leftPanel) => leftPanel ? Left : Right;

    private static CommandId[] Build(CommandId drives) =>
    [
        drives,
        CommandId.Parent,
        CommandId.Hotlist,
        CommandId.History,
        CommandId.NetConnect,
        CommandId.Ftp,
    ];
}
