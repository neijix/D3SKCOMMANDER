using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Controls.FileList;

/// <summary>The header row: titles that sort on click, grips that resize, a menu on right-click.</summary>
public sealed partial class FileListControl
{
    private const double GripWidth = 6;

    private double _dragStartX;
    private double _dragStartWidth;
    private double _dragWidth;

    /// <summary>The menu of the header row; the host fills it each time <see cref="HeaderMenuOpening"/> is raised.</summary>
    public ContextMenu HeaderMenu { get; } = new();

    /// <summary>The header menu is about to open: the host replaces its items.</summary>
    public event EventHandler? HeaderMenuOpening;

    /// <summary>A sortable title was clicked.</summary>
    public event EventHandler<ColumnId>? HeaderClicked;

    /// <summary>A grip was released after a drag: the column and its new width in device-independent pixels.</summary>
    public event EventHandler<ColumnResize>? ColumnResized;

    private void InitHeaderMenu()
    {
        Header.ContextMenu = HeaderMenu;
        Header.Background = Brushes.Transparent;
        HeaderMenu.Opening += (_, _) => HeaderMenuOpening?.Invoke(this, EventArgs.Empty);
    }

    private void BuildHeader()
    {
        Header.Children.Clear();
        Header.ColumnDefinitions.Clear();
        var columns = _list.CurrentColumns;
        for (var i = 0; i < columns.Count; i++)
        {
            Header.ColumnDefinitions.Add(FileListLayout.Definition(columns[i]));
            var block = new TextBlock
            {
                Text = _list.HeaderText(columns[i].HeaderKey),
                Tag = columns[i].HeaderKey,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(i == 0 && _list.ShowsIcons ? FileListLayout.IconLeft + FileListLayout.IconSize + 4 : 4, 0, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(block, i);
            Header.Children.Add(block);
            if (columns[i].Id is { } id && _list.ShowsIcons)
            {
                AddHeaderParts(columns[i], block, i, id);
            }
        }
    }

    private void AddHeaderParts(FileListColumn column, TextBlock block, int index, ColumnId id)
    {
        if (ColumnInfo.IsSortable(id))
        {
            block.Background = Brushes.Transparent;
            block.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(block).Properties.IsLeftButtonPressed)
                {
                    HeaderClicked?.Invoke(this, id);
                    e.Handled = true;
                }
            };
        }

        if (id == ColumnId.Name)
        {
            return;
        }

        var grip = new Border
        {
            Width = GripWidth,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            Tag = id,
        };
        Grid.SetColumn(grip, index);
        grip.PointerPressed += (_, e) => BeginDrag(grip, index, e);
        grip.PointerMoved += (_, e) => Drag(grip, index, e);
        grip.PointerReleased += (_, e) => EndDrag(grip, id, e);
        Header.Children.Add(grip);
    }

    private void BeginDrag(Border grip, int index, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStartX = e.GetPosition(Header).X;
        _dragStartWidth = Header.ColumnDefinitions[index].ActualWidth;
        _dragWidth = _dragStartWidth;
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void Drag(Border grip, int index, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer.Captured, grip))
        {
            return;
        }

        var width = _dragStartWidth + e.GetPosition(Header).X - _dragStartX;
        _dragWidth = Math.Clamp(width, ColumnSet.MinWidth, ColumnSet.MaxWidth);
        Header.ColumnDefinitions[index].Width = new GridLength(_dragWidth);
    }

    private void EndDrag(Border grip, ColumnId id, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer.Captured, grip))
        {
            return;
        }

        e.Pointer.Capture(null);
        e.Handled = true;
        ColumnResized?.Invoke(this, new ColumnResize(id, Math.Round(_dragWidth)));
    }
}

/// <summary>A column of a panel list was resized to <paramref name="Width"/> device-independent pixels.</summary>
public readonly record struct ColumnResize(ColumnId Id, double Width);
