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
    private readonly List<Func<Task<bool>>> _quitGuards = [];
    private readonly Dictionary<CommandId, Func<bool>> _checked = [];
    private readonly List<Func<IShellWidget>> _toolbar = [];
    private readonly List<Func<IShellWidget>> _under = [];
    private readonly List<Func<IShellWidget>> _beside = [];
    private readonly TimeProvider _time;
    private readonly object _statusLock = new();
    private Func<IShellWidget>? _left;
    private Func<IShellWidget>? _right;
    private bool _modulesApplied;
    private bool _startupRan;
    private ITimer? _statusTimer;
    private int _statusGeneration;
    private string? _status;

    public ShellContributions(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised with the new status text, or null when the status line goes back to its idle text. May fire on a timer thread.</summary>
    public event Action<string?>? StatusChanged;

    /// <summary>The text a feature put on the status line, or null when it shows its idle text.</summary>
    public string? Status => Volatile.Read(ref _status);

    /// <summary>How long a status text stays when no command is dispatched.</summary>
    public static TimeSpan StatusLifetime { get; } = TimeSpan.FromSeconds(5);

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

    public void GuardQuit(Func<Task<bool>> ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        _quitGuards.Add(ask);
    }

    /// <summary>The quit guards in the order they were added; the first false stops the quit.</summary>
    public async Task<bool> AskQuitGuardsAsync()
    {
        foreach (var ask in _quitGuards.ToArray())
        {
            if (!await ask().ConfigureAwait(true))
            {
                return false;
            }
        }

        return true;
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

        ClearStatus();
        handler();
        return true;
    }

    public void SetStatus(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        lock (_statusLock)
        {
            _statusTimer?.Dispose();
            var generation = ++_statusGeneration;
            _statusTimer = _time.CreateTimer(_ => ClearStatus(generation), null, StatusLifetime, Timeout.InfiniteTimeSpan);
            Volatile.Write(ref _status, text);
        }

        StatusChanged?.Invoke(text);
    }

    // generation: the timer that fired; a command clears whatever is showing (null).
    private void ClearStatus(int? generation = null)
    {
        lock (_statusLock)
        {
            if (generation is not null && generation != _statusGeneration)
            {
                return;
            }

            _statusTimer?.Dispose();
            _statusTimer = null;
            if (Volatile.Read(ref _status) is null)
            {
                return;
            }

            Volatile.Write(ref _status, null);
        }

        StatusChanged?.Invoke(null);
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
