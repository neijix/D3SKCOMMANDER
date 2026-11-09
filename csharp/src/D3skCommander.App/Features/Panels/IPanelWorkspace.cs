using D3skCommander.App.Shell;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

/// <summary>
/// The two panels other features talk to: where they are, what is selected,
/// and a request to open a folder or list it again.
/// </summary>
public interface IPanelWorkspace
{
    PanelSide ActiveSide { get; }

    PanelSnapshot Read(PanelSide side);

    /// <summary>The active selection and both folders, or null when a listing is in progress.</summary>
    PanelCapture? Capture();

    Task<bool> GoAsync(PanelSide side, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the paths as a virtual folder in a new tab of <paramref name="side"/>, named <paramref name="title"/>
    /// and left through ".." to <paramref name="startFolder"/>. <paramref name="groups"/> (parallel to the paths) keeps duplicate groups together. A workspace without tabs answers false.
    /// </summary>
    Task<bool> OpenResultsAsync(
        PanelSide side,
        string startFolder,
        string title,
        IReadOnlyList<string> paths,
        IReadOnlyList<int>? groups = null,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>
    /// Moves the active panel to the folder that holds <paramref name="path"/> and puts the cursor on it.
    /// A workspace without tabs just goes to the folder.
    /// </summary>
    Task<bool> RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        return GoAsync(ActiveSide, string.IsNullOrEmpty(parent) ? path : parent, cancellationToken);
    }

    /// <summary>The target of the context menu whose command is being dispatched, or null outside one (a workspace without a context menu has none).</summary>
    PanelContextTarget? ContextTarget => null;

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
