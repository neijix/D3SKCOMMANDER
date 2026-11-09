using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.DiskUsage;

/// <summary>The disk usage window over <see cref="DiskUsageViewModel"/>: the list, the ring chart and the buttons.</summary>
public partial class DiskUsageView : UserControl
{
    private readonly DiskUsageViewModel _model = null!;

    public DiskUsageView()
    {
        InitializeComponent();
    }

    public DiskUsageView(DiskUsageViewModel model, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        _model = model;
        InitializeComponent();
        DataContext = model;
        Label(text);
        Chart.Describe = model.Describe;
        Chart.Label = model.LabelOf;
        RowList.ContextMenu = DeleteMenu(text);
        Chart.SegmentClicked += (_, segment) => model.ClickSegment(segment);
        Chart.CenterClicked += (_, _) => model.Up();
        UpButton.Click += (_, _) => model.Up();
        CancelButton.Click += (_, _) => model.Cancel();
        RescanButton.Click += (_, _) => _ = model.RescanAsync();
        ShowButton.Click += (_, _) => _ = model.ShowInPanelAsync();
        RowList.DoubleTapped += (_, _) => model.DescendSelected();
        RowList.KeyDown += OnListKey;
        model.PropertyChanged += OnModelChanged;
        Redraw();
        DetachedFromVisualTree += (_, _) =>
        {
            model.PropertyChanged -= OnModelChanged;
            model.Cancel();
        };
    }

    private void Label(ILocalizer text)
    {
        UpButton.Content = "↑ " + text.Get("diskusage.up");
        CancelButton.Content = text.Get("diskusage.cancel");
        ShowButton.Content = text.Get("diskusage.show_in_panel");
        RescanButton.Content = text.Get("diskusage.rescan");
        NameHeader.Text = text.Get("diskusage.col.name");
        SizeHeader.Text = text.Get("diskusage.col.size");
        ShareHeader.Text = text.Get("diskusage.col.share");
        BarHeader.Text = text.Get("diskusage.col.bar");
    }

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _model.DescendSelected())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Back && _model.Up())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _model.CanDeleteSelected)
        {
            _ = _model.DeleteSelectedAsync();
            e.Handled = true;
        }
    }

    // Right-click on a row: Delete, as the Delete key; nothing opens on a drive or the "smaller files" row.
    private ContextMenu DeleteMenu(ILocalizer text)
    {
        var delete = new MenuItem { Name = "DeleteItem", Header = text.Get("diskusage.delete") };
        delete.Click += (_, _) => _ = _model.DeleteSelectedAsync();
        var menu = new ContextMenu();
        menu.Items.Add(delete);
        menu.Opening += (_, e) => e.Cancel = !_model.CanDeleteSelected;
        return menu;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiskUsageViewModel.Segments) || e.PropertyName == nameof(DiskUsageViewModel.Current))
        {
            Redraw();
        }
    }

    private void Redraw()
    {
        var node = _model.Current;
        var title = node?.Name ?? "";
        var size = node is null ? "" : LocalFileSystem.FormatSize((ulong)node.Bytes);
        Chart.Show(_model.Segments, title, size);
        Dispatcher.UIThread.Post(() => RowList.Focus(), DispatcherPriority.Background);
    }
}
