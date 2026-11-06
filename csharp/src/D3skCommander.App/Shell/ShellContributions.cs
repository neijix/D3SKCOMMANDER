using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

/// <summary>Handlers and extras collected before the main window builds its menu and regions.</summary>
public sealed class ShellContributions : IShellContributions
{
    private readonly Dictionary<CommandId, Action> _handlers = [];
    private readonly Dictionary<string, List<CommandId?>> _extras = [];
    private readonly Dictionary<string, List<Func<IReadOnlyList<ShellAction>>>> _dynamic = [];
    private readonly List<Func<IShellWidget>> _trailing = [];
    private readonly List<Action> _startup = [];
    private readonly Dictionary<CommandId, Func<bool>> _checked = [];
    private readonly List<Func<IShellWidget>> _toolbar = [];
    private readonly List<Func<IShellWidget>> _under = [];
    private readonly List<Func<IShellWidget>> _beside = [];
    private Func<IShellWidget>? _left;
    private Func<IShellWidget>? _right;
    private bool _modulesApplied;
    private bool _startupRan;

    public IReadOnlyList<Func<IShellWidget>> MenuBarTrailing => _trailing;

    public Func<IShellWidget>? LeftPanel => _left;

    public Func<IShellWidget>? RightPanel => _right;

    public IReadOnlyList<Func<IShellWidget>> ToolbarWidgets => _toolbar;

    public IReadOnlyList<Func<IShellWidget>> UnderPanels => _under;

    public IReadOnlyList<Func<IShellWidget>> BesidePanels => _beside;

    public void ApplyModules(IEnumerable<IShellModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (_modulesApplied)
        {
            return;
        }

        _modulesApplied = true;
        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            module.Contribute(this);
        }
    }

    public void RunStartup()
    {
        if (_startupRan)
        {
            return;
        }

        _startupRan = true;
        foreach (var action in _startup)
        {
            action();
        }
    }

    public IReadOnlyList<CommandId?> Extras(string menuKey)
    {
        if (_extras.TryGetValue(menuKey, out var items))
        {
            return items;
        }

        return [];
    }

    public void Handle(CommandId command, Action invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        _handlers[command] = invoke;
    }

    public bool CanExecute(CommandId command) => _handlers.ContainsKey(command);

    public bool Dispatch(CommandId command)
    {
        if (!_handlers.TryGetValue(command, out var handler))
        {
            return false;
        }

        handler();
        return true;
    }

    public void Checked(CommandId command, Func<bool> isChecked)
    {
        ArgumentNullException.ThrowIfNull(isChecked);
        _checked[command] = isChecked;
    }

    /// <summary>The check mark of a toggle command's menu item; null when the command is not a toggle.</summary>
    public bool? CheckedState(CommandId command) =>
        _checked.TryGetValue(command, out var isChecked) ? isChecked() : null;

    public void AppendMenuItem(string menuKey, CommandId command) => ExtrasFor(menuKey).Add(command);

    public void AppendMenuSeparator(string menuKey) => ExtrasFor(menuKey).Add(null);

    public void AppendDynamicItems(string menuKey, Func<IReadOnlyList<ShellAction>> items)
    {
        ArgumentException.ThrowIfNullOrEmpty(menuKey);
        ArgumentNullException.ThrowIfNull(items);
        if (!_dynamic.TryGetValue(menuKey, out var list))
        {
            list = [];
            _dynamic[menuKey] = list;
        }

        list.Add(items);
    }

    public IReadOnlyList<Func<IReadOnlyList<ShellAction>>> DynamicItems(string menuKey)
    {
        if (_dynamic.TryGetValue(menuKey, out var items))
        {
            return items;
        }

        return [];
    }

    public void AddMenuBarTrailing(Func<IShellWidget> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _trailing.Add(build);
    }

    public void OnStartup(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _startup.Add(action);
    }

    public void SetPanel(PanelSide side, Func<IShellWidget> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (side == PanelSide.Left)
        {
            _left = build;
        }
        else if (side == PanelSide.Right)
        {
            _right = build;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown panel side.");
        }
    }

    public void AddRegion(ShellRegion region, Func<IShellWidget> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (region == ShellRegion.Toolbar)
        {
            _toolbar.Add(build);
        }
        else if (region == ShellRegion.UnderPanels)
        {
            _under.Add(build);
        }
        else if (region == ShellRegion.BesidePanels)
        {
            _beside.Add(build);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(region), region, "Unknown shell region.");
        }
    }

    private List<CommandId?> ExtrasFor(string menuKey)
    {
        RequireMenu(menuKey);
        if (!_extras.TryGetValue(menuKey, out var items))
        {
            items = [];
            _extras.Add(menuKey, items);
        }

        return items;
    }

    private static void RequireMenu(string menuKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(menuKey);
        foreach (var menu in CommandCatalog.Menus)
        {
            if (menu.TitleKey == menuKey)
            {
                return;
            }
        }

        throw new ArgumentException($"Unknown menu '{menuKey}'.", nameof(menuKey));
    }
}
