using Avalonia.Controls;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneView
{
    /// <summary>The menu of the last right-click, so a test can read and click its entries.</summary>
    public ContextMenu? FileMenu { get; private set; }

    private void ShowFileMenu(PanelPaneModel model, FileListControl files)
    {
        var actions = model.FileMenu();
        if (actions.Count == 0)
        {
            FileMenu = null;
            return;
        }

        var menu = new ContextMenu();
        foreach (var action in actions)
        {
            if (action.IsRule)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem { Header = action.Header };
            var chosen = action;
            item.Click += (_, _) => chosen.Invoke();
            menu.Items.Add(item);
        }

        FileMenu = menu;
        menu.Open(files);
    }
}
