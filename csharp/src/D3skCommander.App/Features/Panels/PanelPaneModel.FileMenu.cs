using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneModel
{
    /// <summary>The context-menu entries for the file under the cursor (the programs of its associations); empty when there are none.</summary>
    public IReadOnlyList<ShellAction> FileMenu()
    {
        Activate();
        Sync();
        return _panels.FileMenuFor(_side);
    }
}
