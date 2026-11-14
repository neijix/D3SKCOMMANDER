using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Toolbar;

namespace D3skCommander.App.Features.Toolbar;

/// <summary>
/// The column of buttons between the two panels (<see cref="MiddleBarDefaults"/>) and whether it
/// shows, saved in the preferences. A press dispatches the button's command.
/// </summary>
public sealed partial class MiddleBarModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ILocalizer _text;
    private IShellContributions? _shell;

    [ObservableProperty]
    private bool _isVisible = true;

    public MiddleBarModel(AppSettings settings, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(text);
        _settings = settings;
        _text = text;
        IsVisible = settings.Preferences.Current.ShowMiddleBar;
        settings.Preferences.Saved += OnSaved;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    /// <summary>The buttons, in order; a command nobody handles is disabled.</summary>
    public IReadOnlyList<ToolbarButton> Buttons() =>
        [.. MiddleBarDefaults.Items.Select(item => ToolbarButton.Describe(item, _text, _shell))];

    /// <summary>ShowMiddleBar: flips the column and saves the choice.</summary>
    public void Toggle()
    {
        var preferences = _settings.Preferences.Current;
        preferences.ShowMiddleBar = !preferences.ShowMiddleBar;
        IsVisible = preferences.ShowMiddleBar;
        _settings.Preferences.Save(preferences);
    }

    public void Press(ToolbarButton button)
    {
        ArgumentNullException.ThrowIfNull(button);
        if (button.Item.Command is CommandId command)
        {
            _shell?.Dispatch(command);
        }
    }

    private void OnSaved(Preferences saved)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsVisible = saved.ShowMiddleBar;
        }
        else
        {
            Dispatcher.UIThread.Post(() => IsVisible = saved.ShowMiddleBar);
        }
    }
}
