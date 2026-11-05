using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneModel
{
    /// <summary>Local files dropped on this pane from the OS: the drives feature copies them into the folder shown.</summary>
    public void DropFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _panels.DropFiles(_side, paths);
    }
}
