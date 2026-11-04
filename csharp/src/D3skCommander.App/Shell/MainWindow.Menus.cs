using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

public sealed partial class MainWindow
{
    private IReadOnlyList<ShellMenu> BuildMenus()
    {
        var menus = new List<ShellMenu>();
        foreach (var definition in CommandCatalog.Menus)
        {
            menus.Add(CreateTopMenu(definition));
        }

        return menus;
    }

    private ShellMenu CreateTopMenu(MenuDefinition definition)
    {
        var items = new List<ShellMenuEntry>();
        foreach (var item in definition.Items)
        {
            items.Add(CreateEntry(item));
        }

        foreach (var extra in _shell.Extras(definition.TitleKey))
        {
            items.Add(CreateEntry(extra));
        }

        return new ShellMenu(_localizer.Get(definition.TitleKey), items);
    }

    private ShellMenuEntry CreateEntry(CommandId? command)
    {
        if (command is null)
        {
            return new ShellMenuEntry { IsSeparator = true };
        }

        return new ShellMenuEntry
        {
            Header = _localizer.Get(CommandCatalog.LabelKey(command.Value)),
            IsEnabled = _shell.CanExecute(command.Value),
            IsVisible = true,
            Gesture = CommandCatalog.PrimaryGesture(command.Value),
            Command = command.Value,
        };
    }

    private IReadOnlyList<FunctionButton> BuildFunctionKeys()
    {
        var buttons = new List<FunctionButton>();
        foreach (var key in CommandCatalog.FunctionKeys)
        {
            var command = key.Command;
            buttons.Add(new FunctionButton(
                Chord(command) + " " + _localizer.Get(key.LabelKey),
                command,
                _shell.CanExecute(command),
                () => _shell.Dispatch(command)));
        }

        return buttons;
    }

    private static string Chord(CommandId command)
    {
        var gesture = CommandCatalog.PrimaryGesture(command);
        if (gesture is null)
        {
            throw new InvalidOperationException($"Function key {command} has no gesture.");
        }

        return gesture.Value.Format();
    }
}
