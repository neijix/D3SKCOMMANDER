using D3skCommander.App.Dialogs;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Help;

/// <summary>Keys (F1) opens the shortcut list, About shows the version dialog.</summary>
public sealed class HelpCommands
{
    private readonly IDialogOwner _owner;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private KeysWindow? _window;

    public HelpCommands(IDialogOwner owner, IDialogService dialogs, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        _owner = owner;
        _dialogs = dialogs;
        _text = text;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Keys, () => ShowKeys());
        shell.Handle(CommandId.About, () => _ = ShowAboutAsync());
    }

    public Task ShowAboutAsync() => _dialogs.ShowMessageAsync(_text.Get("cmd.about"), AboutInfo.Body(_text));

    /// <summary>Opens the shortcut window over the main window; one already open stays.</summary>
    public KeysWindow? ShowKeys()
    {
        if (_window is { IsVisible: true })
        {
            return _window;
        }

        if (_owner.Current is not { } main)
        {
            return null;
        }

        _window = new KeysWindow(new KeysModel(_text), _text);
        _ = _window.ShowDialog(main);
        return _window;
    }
}
