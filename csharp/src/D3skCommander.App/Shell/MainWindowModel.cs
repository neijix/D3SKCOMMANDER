using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Shell;

/// <summary>The toolkit-neutral main window: the menus, the function-key bar and the key routing.</summary>
public sealed class MainWindowModel
{
    private readonly ShellContributions _shell;
    private readonly ILocalizer _localizer;

    public MainWindowModel(ShellContributions shell, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(localizer);
        _shell = shell;
        _localizer = localizer;
        Menus = BuildMenus();
        FunctionKeys = BuildFunctionKeys();
    }

    public IReadOnlyList<ShellMenu> Menus { get; }

    public IReadOnlyList<FunctionButton> FunctionKeys { get; }

    /// <summary>Dispatches the command bound to the gesture; false when none is bound or it has no handler.</summary>
    public bool KeyPress(GestureKey key, GestureModifiers modifiers)
    {
        var command = CommandCatalog.Match(new Gesture(modifiers, key));
        return command is CommandId id && _shell.Dispatch(id);
    }

    private List<ShellMenu> BuildMenus()
    {
        var menus = new List<ShellMenu>();
        foreach (var definition in CommandCatalog.Menus)
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

            menus.Add(new ShellMenu(_localizer.Get(definition.TitleKey), items, _shell.DynamicItems(definition.TitleKey)));
        }

        return menus;
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

    private List<FunctionButton> BuildFunctionKeys()
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
        var gesture = CommandCatalog.PrimaryGesture(command)
            ?? throw new InvalidOperationException($"Function key {command} has no gesture.");
        return gesture.Format();
    }
}
