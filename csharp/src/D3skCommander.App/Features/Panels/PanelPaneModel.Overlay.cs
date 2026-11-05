using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Panels;

/// <summary>The overlay of a pane: a control a feature shows instead of the file list.</summary>
public sealed partial class PanelPaneModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    private Control? _overlay;

    public bool HasOverlay => Overlay is not null;

    private void InitOverlay()
    {
        Overlay = _panels.OverlayOf(_side);
        _panels.OverlayChanged += (_, side) =>
        {
            if (side == _side)
            {
                Overlay = _panels.OverlayOf(_side);
            }
        };
    }
}
