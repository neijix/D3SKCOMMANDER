using D3skCommander.App.Shell;

namespace D3skCommander.App.ToolWindows;

/// <summary>
/// Opens each tool as its own top-level window, owned by the main window, one per kind.
/// Esc closes it. The next open restores the position and size, clamped onto the screen.
/// </summary>
public interface IToolWindowHost
{
    /// <summary>
    /// View factory for <paramref name="kind"/>. Used the next time that kind is created.
    /// A window that is already open keeps the view it was created with.
    /// </summary>
    void Register(ToolKind kind, Func<IShellWidget> content);

    /// <summary>Opens <paramref name="kind"/>, or activates the window already open for it.</summary>
    ToolWindow Open(ToolKind kind);

    bool IsOpen(ToolKind kind);
}
