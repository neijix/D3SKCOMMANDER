using Avalonia;

namespace D3skCommander.App.Features.WindowsMenu;

/// <summary>How the native Explorer menu ended.</summary>
public enum ExplorerMenuOutcome
{
    /// <summary>A command was chosen and ran.</summary>
    Invoked,

    /// <summary>The menu was closed without a choice.</summary>
    Dismissed,

    /// <summary>The menu could not be built or the command failed; <see cref="ExplorerMenuResult.Reason"/> says why.</summary>
    Failed,
}

/// <summary>Why the native menu failed; the module localizes it (en/fr).</summary>
public enum ExplorerMenuFailure
{
    /// <summary>Not Windows.</summary>
    WindowsOnly,

    /// <summary>No path to show.</summary>
    NothingToShow,

    /// <summary>A path is no valid local path (a provider path, a bad character, too long).</summary>
    InvalidPath,

    /// <summary>The items are not all in one folder.</summary>
    NotOneFolder,

    /// <summary>The hidden host window could not be created.</summary>
    HostWindow,

    /// <summary>The shell (COM) refused; <see cref="ExplorerMenuResult.Detail"/> holds the technical text (an HRESULT).</summary>
    Shell,
}

/// <param name="Outcome">How the menu ended.</param>
/// <param name="Reason">Why it failed (only with <see cref="ExplorerMenuOutcome.Failed"/>).</param>
/// <param name="Detail">A technical text to show after the localized reason (an HRESULT, a path); never the only explanation.</param>
public readonly record struct ExplorerMenuResult(ExplorerMenuOutcome Outcome, ExplorerMenuFailure? Reason = null, string? Detail = null)
{
    public static ExplorerMenuResult Invoked => new(ExplorerMenuOutcome.Invoked);

    public static ExplorerMenuResult Dismissed => new(ExplorerMenuOutcome.Dismissed);

    public static ExplorerMenuResult Failed(ExplorerMenuFailure reason, string? detail = null) => new(ExplorerMenuOutcome.Failed, reason, detail);
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
