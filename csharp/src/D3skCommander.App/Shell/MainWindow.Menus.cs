using Avalonia.Controls;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

public partial class MainWindow
{
    private void BuildMenu()
    {
        foreach (var definition in CommandCatalog.Menus)
        {
            MainMenu.Items.Add(CreateTopMenu(definition));
        }

        foreach (var build in _shell.MenuBarTrailing)
        {
            MenuBarTrailing.Children.Add(RequireControl(build));
        }
    }

    private MenuItem CreateTopMenu(MenuDefinition definition)
    {
        var top = new MenuItem { Header = _localizer.Get(definition.TitleKey) };
        foreach (var item in definition.Items)
        {
            AddMenuEntry(top, item);
        }

        foreach (var extra in _shell.Extras(definition.TitleKey))
        {
            AddMenuEntry(top, extra);
        }

        return top;
    }

    private void AddMenuEntry(MenuItem top, CommandId? command)
    {
        if (command is null)
        {
            top.Items.Add(new Separator());
            return;
        }

        top.Items.Add(CreateCommandItem(command.Value));
    }

    private MenuItem CreateCommandItem(CommandId command)
    {
        var item = new MenuItem
        {
            Header = _localizer.Get(CommandCatalog.LabelKey(command)),
            IsEnabled = _shell.CanExecute(command),
            InputGesture = GestureMap.ToKeyGesture(CommandCatalog.PrimaryGesture(command)),
        };
        item.Click += (_, _) => _shell.Dispatch(command);
        return item;
    }
}
