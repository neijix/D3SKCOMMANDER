using Avalonia;
using D3skCommander.App.Features.Launch;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneModel
{
    /// <summary>Raised when the <see cref="CommandId.ContextMenu"/> key (Shift+F10) asks this pane for the menu of its cursor row.</summary>
    public event EventHandler? ContextMenuRequested;

    /// <summary>
    /// The view hands over where the cursor row ends on screen (bottom-left), so a native menu command opens there, and
    /// starts listening for the <see cref="CommandId.ContextMenu"/> key.
    /// </summary>
    public void BindContextMenu(Func<PixelPoint?> cursorRowPoint)
    {
        ArgumentNullException.ThrowIfNull(cursorRowPoint);
        _panels.SetCursorRowPoint(_side, cursorRowPoint);
        _panels.ContextMenuRequested -= OnContextMenuRequested;
        _panels.ContextMenuRequested += OnContextMenuRequested;
    }

    /// <summary>The context-menu entries for the file under the cursor (the programs of its associations); empty when there are none.</summary>
    public IReadOnlyList<ShellAction> FileMenu()
    {
        Activate();
        Sync();
        return _panels.FileMenuFor(_side);
    }

    /// <summary>
    /// The context menu of the cursor row (or of empty space): Core's entries for the target, as labelled actions
    /// whose invocation dispatches the command on that target. Null when the panel has no folder yet.
    /// </summary>
    /// <param name="emptySpace">True for a click below the rows.</param>
    /// <param name="point">The screen point the menu opens at; a native menu command opens there too.</param>
    public IReadOnlyList<ShellAction>? ContextMenu(bool emptySpace, PixelPoint? point)
    {
        Activate();
        Sync();
        if (_panels.ContextTargetFor(_side, emptySpace) is not { } target)
        {
            return null;
        }

        var hasFiles = _panels.ClipboardHasFiles?.Invoke() ?? true;
        var entries = PanelContextMenu.Build(target, CurrentPlatform(), hasFiles, PwshFound());
        var items = new List<ShellAction>();
        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case MenuEntryKind.Separator:
                    items.Add(ShellAction.Rule);
                    break;
                case MenuEntryKind.OpenWithSubmenu:
                    items.Add(OpenWithSubmenu(target, point));
                    break;
                default:
                    items.Add(CommandAction(entry.Command!.Value, target, point));
                    break;
            }
        }

        return items;
    }

    private void OnContextMenuRequested(object? sender, PanelSide side)
    {
        if (side == _side)
        {
            ContextMenuRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private ShellAction CommandAction(CommandId command, PanelContextTarget target, PixelPoint? point)
    {
        var header = _text.Get(CommandCatalog.LabelKey(command));
        if (CommandCatalog.PrimaryGesture(command) is { } gesture)
        {
            header += "    " + gesture.Format();
        }

        return new ShellAction(header, () => RunInContext(command, target, point), Command: command);
    }

    // Today's association items, then the Open with dialog command: the submenu is never empty.
    private ShellAction OpenWithSubmenu(PanelContextTarget target, PixelPoint? point)
    {
        var children = new List<ShellAction>(_panels.FileMenuFor(_side));
        if (children.Count > 0)
        {
            children.Add(ShellAction.Rule);
        }

        children.Add(new ShellAction(_text.Get(CommandCatalog.LabelKey(CommandId.OpenWith)), () => RunInContext(CommandId.OpenWith, target, point)));
        return new ShellAction(_text.Get(CommandCatalog.LabelKey(CommandId.OpenWith)), () => { }, Children: children);
    }

    // The cursor is already on the clicked row (P8), so a command that reads the cursor and marks agrees with the context target.
    private void RunInContext(CommandId command, PanelContextTarget target, PixelPoint? point)
    {
        Activate();
        _panels.RunInContext(target, point, () => _dispatch?.Invoke(command));
    }

    private static MenuPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return MenuPlatform.Windows;
        }

        return OperatingSystem.IsMacOS() ? MenuPlatform.MacOS : MenuPlatform.Linux;
    }

    // The same PATH probe as the terminal commands; only Linux asks (Windows always offers PowerShell, macOS never).
    private static bool PwshFound() => OperatingSystem.IsLinux() && ExecutablePath.IsOnPath("pwsh");
}
