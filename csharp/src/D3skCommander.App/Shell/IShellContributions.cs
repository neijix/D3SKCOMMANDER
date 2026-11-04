using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

/// <summary>What a feature may register: handlers, menu extras, a trailing menu-bar item, startup and regions.</summary>
public interface IShellContributions
{
    void Handle(CommandId command, Action invoke);

    bool CanExecute(CommandId command);

    bool Dispatch(CommandId command);

    void AppendMenuItem(string menuKey, CommandId command);

    void AppendMenuSeparator(string menuKey);

    void AddMenuBarTrailing(Func<IShellWidget> build);

    void OnStartup(Action action);

    void SetPanel(PanelSide side, Func<IShellWidget> build);

    void AddRegion(ShellRegion region, Func<IShellWidget> build);
}
