using D3skCommander.App.Shell;

namespace D3skCommander.App.Dialogs;

/// <summary>The open main window. A modal is owned by that window.</summary>
public sealed class ShellDialogOwner : IDialogOwner
{
    public MainWindow? Current => ToolWindowOwners.LatestVisible();
}
