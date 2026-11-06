using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Features.Launch;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Toolbar;

namespace D3skCommander.App.Features.Toolbar;

/// <summary>
/// The main toolbar: the buttons and whether it shows, both read from the preferences. A save of the
/// preferences (the toggle, the Settings window) updates it. A press resolves the button
/// (<see cref="ToolbarActions"/>) and dispatches a command or starts the processes.
/// </summary>
public sealed partial class ToolbarModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ILocalizer _text;
    private readonly LaunchCommands _launch;
    private IShellContributions? _shell;
    private List<ToolbarItem> _items = [];

    [ObservableProperty]
    private bool _isVisible = true;

    public ToolbarModel(AppSettings settings, ILocalizer text, LaunchCommands launch)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(launch);
        _settings = settings;
        _text = text;
        _launch = launch;
        Load(settings.Preferences.Current);
        settings.Preferences.Saved += OnSaved;
    }

    /// <summary>The buttons now, in order. Rebuilt when the preferences change; see <see cref="ButtonsChanged"/>.</summary>
    public IReadOnlyList<ToolbarButton> Buttons { get; private set; } = [];

    public event Action? ButtonsChanged;

    public string CustomizeCaption => _text.Get(CommandCatalog.LabelKey(CommandId.CustomizeToolbar));

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    /// <summary>ShowToolbar: flips the toolbar and saves the choice.</summary>
    public void Toggle()
    {
        var preferences = _settings.Preferences.Current;
        preferences.ShowToolbar = !preferences.ShowToolbar;
        IsVisible = preferences.ShowToolbar;
        _settings.Preferences.Save(preferences);
    }

    public void Customize() => _shell?.Dispatch(CommandId.CustomizeToolbar);

    /// <summary>What a button does when pressed. A command nobody handles does nothing.</summary>
    public void Press(ToolbarButton button)
    {
        ArgumentNullException.ThrowIfNull(button);
        var action = ToolbarActions.Resolve(button.Item, _launch.ToolbarContext());
        if (action.Command is CommandId command)
        {
            _shell?.Dispatch(command);
        }
        else if (action.Processes.Count > 0)
        {
            _launch.Run(action.Processes);
        }
    }

    private void OnSaved(Preferences saved)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Load(saved);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Load(saved));
        }
    }

    private void Load(Preferences preferences)
    {
        IsVisible = preferences.ShowToolbar;
        if (!_items.SequenceEqual(preferences.Toolbar))
        {
            _items = [.. preferences.Toolbar];
            Refresh();
        }
    }

    /// <summary>Builds <see cref="Buttons"/> again, asking the shell which commands can run now.</summary>
    public void Refresh()
    {
        Buttons = [.. _items.Select(Describe)];
        ButtonsChanged?.Invoke();
    }

    private ToolbarButton Describe(ToolbarItem item)
    {
        var enabled = item.Kind != ToolbarItemKind.Command
            || item.Command is not CommandId command
            || _shell is null
            || _shell.CanExecute(command);
        return new ToolbarButton(item, item.EffectiveIcon(), TooltipOf(item), enabled);
    }

    /// <summary>The user's text, else the command's label and its key, else the name of what the button starts.</summary>
    private string TooltipOf(ToolbarItem item)
    {
        if (item.Tooltip.Length > 0)
        {
            return item.Tooltip;
        }

        if (item.Kind == ToolbarItemKind.Command && item.Command is CommandId command)
        {
            var label = _text.Get(CommandCatalog.LabelKey(command));
            return CommandCatalog.PrimaryGesture(command) is Gesture gesture ? label + " (" + gesture.Format() + ")" : label;
        }

        if (item.Kind == ToolbarItemKind.UserCommand)
        {
            return item.UserCommandName;
        }

        return item.Kind == ToolbarItemKind.Program ? Path.GetFileName(item.Program.TrimEnd('/', '\\')) : "";
    }
}
