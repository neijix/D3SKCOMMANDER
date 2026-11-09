using Avalonia;

namespace D3skCommander.App.Features.WindowsMenu;

/// <summary>How the native Explorer menu ended.</summary>
public enum ExplorerMenuOutcome
{
    /// <summary>A command was chosen and ran.</summary>
    Invoked,

    /// <summary>The menu was closed without a choice.</summary>
    Dismissed,

    /// <summary>The menu could not be built or the command failed; <see cref="ExplorerMenuResult.Detail"/> says why.</summary>
    Failed,
}

public readonly record struct ExplorerMenuResult(ExplorerMenuOutcome Outcome, string? Detail = null)
{
    public static ExplorerMenuResult Invoked => new(ExplorerMenuOutcome.Invoked);

    public static ExplorerMenuResult Dismissed => new(ExplorerMenuOutcome.Dismissed);

    public static ExplorerMenuResult Failed(string detail) => new(ExplorerMenuOutcome.Failed, detail);
}

/// <summary>The native Windows Explorer context menu for files. Only a Windows implementation does anything.</summary>
public interface IExplorerMenu
{
    /// <summary>
    /// Shows the menu for <paramref name="paths"/> (all in one folder) at a screen point, waits until it closes
    /// and runs the chosen command. Must be called on the UI thread. <paramref name="screenPoint"/> null = the mouse.
    /// </summary>
    ExplorerMenuResult Show(IReadOnlyList<string> paths, PixelPoint? screenPoint, IntPtr ownerWindow);
}
