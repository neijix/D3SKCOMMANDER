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

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
