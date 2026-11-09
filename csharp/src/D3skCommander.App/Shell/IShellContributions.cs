using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

/// <summary>What a feature may register: handlers, menu extras, a trailing menu-bar item, startup and regions.</summary>
public interface IShellContributions
{
    void Handle(CommandId command, Action invoke);

    bool CanExecute(CommandId command);

    bool Dispatch(CommandId command);

    /// <summary>Shows a text on the main window's status line until the next dispatched command or 5 seconds, whichever comes first.</summary>
    void SetStatus(string text);

    /// <summary>Gives a toggle command a check mark in its menu items, read each time a menu opens.</summary>
    void Checked(CommandId command, Func<bool> isChecked);

    void AppendMenuItem(string menuKey, CommandId command);

    void AppendMenuSeparator(string menuKey);

    /// <summary>Items a menu reads again each time it opens, shown after its commands and extras.</summary>
    void AppendDynamicItems(string menuKey, Func<IReadOnlyList<ShellAction>> items);

    void AddMenuBarTrailing(Func<IShellWidget> build);

    void OnStartup(Action action);

    void SetPanel(PanelSide side, Func<IShellWidget> build);

    void AddRegion(ShellRegion region, Func<IShellWidget> build);
}
