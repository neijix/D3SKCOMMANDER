using Avalonia.Controls;
using D3skCommander.App.Features.Tree;
using D3skCommander.Core.Settings;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneView
{
    private FolderTreeView? _tree;

    /// <summary>The folder tree beside the list, or null before the pane has a model.</summary>
    public FolderTreeView? Tree => _tree;

    private void BuildTree(PanelPaneModel model)
    {
        _tree = new FolderTreeView(model.Tree, model.OpenInOtherPanel);
        TreeHost.Content = _tree;
        Body.ColumnDefinitions[0].MinWidth = 0;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PanelPaneModel.ShowTree) or nameof(PanelPaneModel.TreeWidth))
            {
                ApplyTree(model);
            }
        };
        TreeSplitter.DragCompleted += (_, _) => model.SaveTreeWidth(Body.ColumnDefinitions[0].ActualWidth);
        ApplyTree(model);
    }

    private void ApplyTree(PanelPaneModel model)
    {
        var column = Body.ColumnDefinitions[0];
        column.MinWidth = model.ShowTree ? Preferences.MinTreeWidth : 0;
        column.Width = model.ShowTree ? new GridLength(model.TreeWidth) : new GridLength(0);
    }
}
