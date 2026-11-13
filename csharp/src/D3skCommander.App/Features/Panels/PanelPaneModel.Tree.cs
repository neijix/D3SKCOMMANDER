using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.Tree;
using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneModel
{
    private bool _treeShown;
    private bool _treeRootsAsked;
    private string? _treePath;
    private int _treeGeneration = -1;

    [ObservableProperty]
    private bool _showTree;
    [ObservableProperty]
    private double _treeWidth = Preferences.DefaultTreeWidth;

    /// <summary>The folder tree beside this panel's file list.</summary>
    public FolderTreeModel Tree { get; private set; } = null!;

    /// <summary>The splitter was dragged: the width is kept for the next start.</summary>
    public void SaveTreeWidth(double width)
    {
        TreeWidth = width;
        _panels.SetTreeWidth(_side, width);
    }

    /// <summary>Context menu "Open in the other panel".</summary>
    public void OpenInOtherPanel(FolderTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _ = _panels.GoAsync(_side == PanelSide.Left ? PanelSide.Right : PanelSide.Left, node.Path);
    }

    private void InitTree(IDriveSource? drives, ILocalizer text)
    {
        Tree = new FolderTreeModel(_panels.TreeLoader(), drives, text, () => _panels.ShowHidden, GoFromTree);
        ShowTree = _panels.ShowTree(_side);
        TreeWidth = _panels.TreeWidth(_side);
    }

    // A node was chosen in the tree: this panel becomes active and goes there.
    private void GoFromTree(string path)
    {
        Activate();
        _ = _panels.GoAsync(_side, path);
    }

    // The tree follows the panel: a new folder selects its node, a new listing of the same folder reads its node again.
    private void TickTree(PanelSnapshot snap)
    {
        ShowTree = _panels.ShowTree(_side);
        TreeWidth = _panels.TreeWidth(_side);
        if (ComputerAddress.IsAddress(snap.Path))
        {
            // Computer is no tree node: the tree keeps the last folder selected. A panel that starts
            // there still needs the roots, which only a follow would read otherwise.
            if (ShowTree && Tree.Roots.Count == 0 && !_treeRootsAsked)
            {
                _treeRootsAsked = true;
                _ = Tree.LoadRootsAsync();
            }

            return;
        }

        if (!ShowTree || snap.Path.Length == 0)
        {
            _treeShown = false;
            return;
        }

        if (!_treeShown || snap.Path != _treePath)
        {
            _treeShown = true;
            _treePath = snap.Path;
            _treeGeneration = snap.Generation;
            _ = Tree.FollowAsync(snap.Path);
        }
        else if (snap.Generation != _treeGeneration)
        {
            _treeGeneration = snap.Generation;
            _ = Tree.RefreshAsync(snap.Path);
        }
    }
}
