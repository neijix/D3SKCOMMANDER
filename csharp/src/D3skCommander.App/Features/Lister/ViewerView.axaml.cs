using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using D3skCommander.Core.Viewer;

namespace D3skCommander.App.Features.Lister;

/// <summary>
/// Draws <see cref="ViewerViewModel"/>: mode switch, find or filter bar, virtualized rows of the
/// loaded page, the scroll bar over the whole file, status line. Crossing a page edge with the
/// wheel or the keys loads the neighbouring page.
/// </summary>
public partial class ViewerView : UserControl
{
    // The ListBoxItem padding (4 px each side, set in ViewerView.axaml) is not text width.
    private const double RowPadding = 8;
    private const int Sample = 32;
    private ViewerViewModel? _watched;

    public ViewerView()
    {
        InitializeComponent();
        ModeBox.SelectionChanged += OnModeChanged;
        NextButton.Click += (_, _) => _ = Model?.FindAsync(true);
        PreviousButton.Click += (_, _) => _ = Model?.FindAsync(false);
        FindBox.KeyDown += OnFindKey;
        RowList.SelectionChanged += OnRowSelected;
        RowList.KeyDown += OnListKey;
        RowList.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Bubble, true);
        RowList.AddHandler(ScrollViewer.ScrollChangedEvent, OnListScrolled);
        PageScroll.Scroll += OnPageScroll;
        RowList.SizeChanged += (_, _) => UpdateColumns();
        DataContextChanged += OnDataContextChanged;
    }

    private ViewerViewModel? Model => DataContext as ViewerViewModel;

    /// <summary>
    /// Tells the model how many characters fit across a row: the list's text width over the width
    /// of one character of the row font (fixed pitch), measured, not assumed.
    /// </summary>
    private void UpdateColumns()
    {
        if (Model is not { } model)
        {
            return;
        }

        var width = RowList.Scroll is ScrollViewer { Viewport.Width: > 0 } viewer ? viewer.Viewport.Width : RowList.Bounds.Width;
        model.SetColumns(ViewerText.ColumnsIn(width - RowPadding, CharWidth()));
    }

    /// <summary>The advance of one character of the row font, at the list's font size.</summary>
    internal double CharWidth()
    {
        var family = this.TryFindResource("MonoFont", out var found) && found is FontFamily mono ? mono : FontFamily.Default;
        var sample = new FormattedText(
            new string('0', Sample),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(family),
            RowList.FontSize,
            null);
        return sample.WidthIncludingTrailingWhitespace / Sample;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Closed += OnWindowClosed;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Closed -= OnWindowClosed;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowClosed(object? sender, EventArgs e) => Model?.ReleaseImage();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_watched is not null)
        {
            _watched.RevealRequested -= OnReveal;
        }

        _watched = Model;
        if (_watched is not null)
        {
            _watched.RevealRequested += OnReveal;
            UpdateColumns();
        }
    }

    private void OnReveal(int row)
    {
        if (row >= 0 && row < RowList.ItemCount)
        {
            RowList.ScrollIntoView(row);
        }
    }

    private void OnModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ModeBox.SelectedItem is ViewerModeChoice choice && Model is { } model && model.SelectedMode != choice)
        {
            _ = model.SetModeAsync(choice.Mode);
        }
    }

    private void OnFindKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = Model?.FindAsync(!e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
    }

    private void OnRowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (RowList.SelectedIndex >= 0)
        {
            RowList.ScrollIntoView(RowList.SelectedIndex);
        }
    }

    private void OnPageScroll(object? sender, ScrollEventArgs e)
    {
        if (e.ScrollEventType != ScrollEventType.ThumbTrack)
        {
            _ = Model?.JumpAsync(e.NewValue);
        }
    }

    private void OnListScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is ScrollViewer { Extent.Height: var extent, Viewport.Height: var viewport, Offset.Y: var offset }
            && extent > viewport)
        {
            Model?.SetScrollFraction(offset / (extent - viewport));
        }
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Model is not { HasPageScroll: true } model || RowList.Scroll is not ScrollViewer viewer)
        {
            return;
        }

        var atBottom = viewer.Offset.Y + viewer.Viewport.Height >= viewer.Extent.Height - 1;
        if (e.Delta.Y < 0 && atBottom)
        {
            _ = model.NextPageAsync();
        }
        else if (e.Delta.Y > 0 && viewer.Offset.Y <= 0)
        {
            _ = model.PreviousPageAsync();
        }
    }

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        if (Model is not { HasPageScroll: true } model)
        {
            return;
        }

        var last = RowList.ItemCount - 1;
        var onLast = RowList.SelectedIndex >= last;
        var onFirst = RowList.SelectedIndex <= 0;
        if (e.Key is Key.Down or Key.PageDown && onLast)
        {
            _ = model.NextPageAsync();
        }
        else if (e.Key is Key.Up or Key.PageUp && onFirst)
        {
            _ = model.PreviousPageAsync();
        }
        else if (e.Key == Key.End && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = model.GoToEndAsync();
        }
        else if (e.Key == Key.Home && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = model.JumpAsync(0);
        }
    }
}
