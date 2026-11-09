namespace D3skCommander.Core.Commands;

/// <summary>
/// The icon buttons in front of each panel's path box, left to right: Home (drives and favorites), parent,
/// history, network, FTP and the preview mode. The bar runs a button's command on the panel it sits on. The
/// drive menu stays on its keys; the drive buttons and the favorites sit in the drive bar above.
/// </summary>
internal static class PanelBarLayout
{
    private static readonly CommandId[] Buttons =
    [
        CommandId.Computer,
        CommandId.Parent,
        CommandId.History,
        CommandId.NetConnect,
        CommandId.Ftp,
        CommandId.PreviewPanel,
    ];

    /// <summary>The same buttons on both sides.</summary>
    public static IReadOnlyList<CommandId> For(bool leftPanel)
    {
        _ = leftPanel;
        return Buttons;
    }
}
