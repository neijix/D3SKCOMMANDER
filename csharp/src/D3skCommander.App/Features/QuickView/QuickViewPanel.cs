using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using D3skCommander.App.Features.Lister;
using D3skCommander.Core.Viewer;

namespace D3skCommander.App.Features.QuickView;

/// <summary>
/// What the quick view puts over a pane: the lister's own view for a file, a short summary for a
/// folder, nothing for the rest.
/// </summary>
internal sealed class QuickViewPanel : ContentControl
{
    // The list's row padding (8 px) and its scroll bar: not text width.
    private const double Chrome = 32;
    private readonly Control _viewer;
    private readonly ViewerViewModel _model;
    private readonly TextBlock _summary = new()
    {
        Margin = new Avalonia.Thickness(8),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Top,
    };

    public QuickViewPanel(ViewerViewModel viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        _model = viewer;
        _viewer = viewer.CreateContent();
        SizeChanged += (_, e) => FitColumns(e.NewSize.Width);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public bool ShowsViewer => ReferenceEquals(Content, _viewer);

    public string SummaryText => _summary.Text ?? "";

    public void ShowViewer() => Content = _viewer;

    public void ShowSummary(string text)
    {
        _summary.Text = text;
        Content = _summary;
    }

    /// <summary>
    /// The viewer wraps at the width of the pane it sits in: the view measures it at its first
    /// layout, before the pane has its size, so the panel tells the model again when it resizes.
    /// </summary>
    private void FitColumns(double width)
    {
        if (_viewer is ViewerView view && width > Chrome)
        {
            _model.SetColumns(ViewerText.ColumnsIn(width - Chrome, view.CharWidth()));
        }
    }

    public void ShowNothing() => Content = null;
}
