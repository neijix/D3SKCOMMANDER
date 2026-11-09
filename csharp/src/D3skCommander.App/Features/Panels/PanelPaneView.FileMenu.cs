using Avalonia;
using Avalonia.Controls;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneView
{
    /// <summary>The menu of the last right-click (or Shift+F10), so a test can read and click its entries.</summary>
    public ContextMenu? FileMenu { get; private set; }

    // A right-click on a row or on empty space: the cursor is already on the row, the menu opens at the pointer.
    private void ShowFileMenu(PanelPaneModel model, FileListControl files, FileListContextRequest request) =>
        Show(model, files, request.Index < 0, request.ScreenPoint, anchor: null);

    // The context-menu key: the cursor row's menu, opened under that row.
    private void ShowCursorMenu(PanelPaneModel model, FileListControl files)
    {
        var index = files.List.CursorIndex;
        Show(model, files, emptySpace: false, files.RowScreenBottomLeft(index), files.RowAnchor(index));
    }

    private void Show(PanelPaneModel model, FileListControl files, bool emptySpace, PixelPoint? point, Control? anchor)
    {
        var actions = model.ContextMenu(emptySpace, point);
        if (actions is null or { Count: 0 })
        {
            FileMenu = null;
            return;
        }

        var menu = new ContextMenu();
        foreach (var action in actions)
        {
            menu.Items.Add(CreateItem(action));
        }

        FileMenu = menu;
        if (anchor is not null)
        {
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            menu.Open(anchor);
            return;
        }

        menu.Open(files);
    }

    private static Control CreateItem(ShellAction action)
    {
        if (action.IsRule)
        {
            return new Separator();
        }

        var item = new MenuItem { Header = action.Header, Tag = action };
        if (action.Children is not null)
        {
            foreach (var child in action.Children)
            {
                item.Items.Add(CreateItem(child));
            }

            return item;
        }

        item.Click += (_, _) => action.Invoke();
        return item;
    }
}
