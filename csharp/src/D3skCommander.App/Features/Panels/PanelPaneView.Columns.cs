using Avalonia.Controls;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneView
{
    private void BindHeader(PanelPaneModel model, Controls.FileList.FileListControl files)
    {
        files.HeaderClicked += (_, id) => model.SortBy(id);
        files.ColumnResized += (_, e) => model.SaveWidth(e.Id, e.Width);
        files.HeaderMenuOpening += (_, _) => FillHeaderMenu(model, files.HeaderMenu);
    }

    private static void FillHeaderMenu(PanelPaneModel model, ContextMenu menu)
    {
        menu.Items.Clear();
        var current = model.CurrentColumnSet;
        foreach (var name in model.ColumnSetNames)
        {
            var item = new MenuItem
            {
                Header = name,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
            };
            var chosen = name;
            item.Click += (_, _) => model.UseColumnSet(chosen);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var edit = new MenuItem { Header = model.ColumnSetsEditText };
        edit.Click += (_, _) => model.EditColumnSets();
        menu.Items.Add(edit);
    }
}
