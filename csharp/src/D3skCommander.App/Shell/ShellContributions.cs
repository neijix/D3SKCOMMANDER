using Avalonia.Controls;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

/// <summary>Handlers and extras collected before the main window builds its menu and regions.</summary>
public sealed class ShellContributions : IShellContributions
{
    private readonly Dictionary<CommandId, Action> _handlers = [];
    private readonly Dictionary<string, List<CommandId?>> _extras = [];
    private readonly List<Func<Control>> _trailing = [];
    private readonly List<Action> _startup = [];
    private readonly List<Func<Control>> _under = [];
    private readonly List<Func<Control>> _beside = [];
    private Func<Control>? _left;
    private Func<Control>? _right;
    private bool _modulesApplied;
    private bool _startupRan;

    public IReadOnlyList<Func<Control>> MenuBarTrailing => _trailing;

    public Func<Control>? LeftPanel => _left;

    public Func<Control>? RightPanel => _right;

    public IReadOnlyList<Func<Control>> UnderPanels => _under;

    public IReadOnlyList<Func<Control>> BesidePanels => _beside;

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

    public void AppendMenuItem(string menuKey, CommandId command) => ExtrasFor(menuKey).Add(command);

    public void AppendMenuSeparator(string menuKey) => ExtrasFor(menuKey).Add(null);

    public void AddMenuBarTrailing(Func<Control> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _trailing.Add(build);
    }

    public void OnStartup(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _startup.Add(action);
    }

    public void SetPanel(PanelSide side, Func<Control> build)
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

    public void AddRegion(ShellRegion region, Func<Control> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (region == ShellRegion.UnderPanels)
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
