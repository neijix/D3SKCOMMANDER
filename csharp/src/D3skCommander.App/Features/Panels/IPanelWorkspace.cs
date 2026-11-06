using D3skCommander.App.Shell;

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

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
