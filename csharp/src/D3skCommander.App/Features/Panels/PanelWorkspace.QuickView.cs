using Avalonia.Controls;
using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Panels;

/// <summary>A control a feature puts over a pane's file list (the quick view), one per side.</summary>
public sealed partial class PanelWorkspace
{
    private readonly Control?[] _overlays = new Control?[2];

    /// <summary>Raised with the side whose overlay was set or cleared.</summary>
    public event EventHandler<PanelSide>? OverlayChanged;

    public Control? OverlayOf(PanelSide side) => _overlays[side == PanelSide.Left ? 0 : 1];

    /// <summary>Shows <paramref name="overlay"/> instead of the file list of a side; null brings the list back.</summary>
    public void SetOverlay(PanelSide side, Control? overlay)
    {
        _overlays[side == PanelSide.Left ? 0 : 1] = overlay;
        OverlayChanged?.Invoke(this, side);
    }
}
