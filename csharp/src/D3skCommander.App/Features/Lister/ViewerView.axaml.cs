using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using D3skCommander.Core.Viewer;

namespace D3skCommander.App.Features.Lister;

/// <summary>
/// Draws <see cref="ViewerViewModel"/>: mode switch, find or filter bar, virtualized rows of the
/// loaded page, the scroll bar over the whole file, status line. Crossing a page edge with the
/// wheel or the keys loads the neighbouring page.
/// </summary>
public partial class ViewerView : UserControl
{
    private const double CharWidth = 8;
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
        DataContextChanged += OnDataContextChanged;
    }

    private ViewerViewModel? Model => DataContext as ViewerViewModel;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Model?.SetColumns((int)((e.NewSize.Width - 100) / CharWidth));
    }

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

/// <summary>Monospace text of a table row: cells padded to the table's width hints.</summary>
internal static class ViewerRowText
{
    private const string Separator = " | ";

    public static string Header(ViewerTable table) => Join(table, table.Columns);

    public static string Row(ViewerTable table, int index) => Join(table, table.Rows[index]);

    private static string Join(ViewerTable table, IReadOnlyList<string> cells)
    {
        var parts = new string[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            var width = i < table.Widths.Count ? table.Widths[i] : 0;
            parts[i] = cells[i].PadRight(width);
        }

        return string.Join(Separator, parts);
    }
}

/// <summary>A read-only list whose items are made when asked for, so a huge file costs nothing until scrolled.</summary>
internal sealed class LazyRows : IList, IReadOnlyList<ViewerLine>
{
    private readonly Func<int, ViewerLine> _make;

    public LazyRows(int count, Func<int, ViewerLine> make)
    {
        Count = count;
        _make = make;
    }

    public int Count { get; }

    public bool IsReadOnly => true;

    public bool IsFixedSize => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public ViewerLine this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _make(index);
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    public IEnumerator<ViewerLine> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return _make(i);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains(object? value) => IndexOf(value) >= 0;

    public int IndexOf(object? value)
    {
        for (var i = 0; i < Count; i++)
        {
            if (Equals(_make(i), value))
            {
                return i;
            }
        }

        return -1;
    }

    public void CopyTo(Array array, int index)
    {
        foreach (var row in this)
        {
            array.SetValue(row, index++);
        }
    }

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();
}
