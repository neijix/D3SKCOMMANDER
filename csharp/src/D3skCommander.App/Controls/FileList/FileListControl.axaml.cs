using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Controls.FileList;

/// <summary>
/// The virtualized file list for the full and brief views and for the other tables. It only
/// shows and dispatches: the rules (marks, cursor, keys, rename) stay in <see cref="FileList"/>.
/// </summary>
public sealed partial class FileListControl : UserControl
{
    private readonly FileList _list;

    public FileListControl()
        : this(new FileList())
    {
    }

    public FileListControl(ILocalizer localizer)
        : this(new FileList(localizer))
    {
    }

    public FileListControl(FileList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        _list = list;
        DataContext = list;
        InitializeComponent();
        Rows.ItemTemplate = new FuncDataTemplate<FileListRow>((_, _) => new FileListRowView(), true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Rows.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        StartThumbs();
        InitHeaderMenu();
        _list.TableChanged += OnTableChanged;
        _list.CursorRevealed += OnCursorRevealed;
        ShowTable();
    }

    /// <summary>The rules behind the control: entries, view, marks, cursor and rename.</summary>
    public FileList List => _list;

    /// <summary>A double-click landed on a row (the row index).</summary>
    public event EventHandler<int>? RowActivated;

    /// <summary>A right-click landed on a row (the row index); the cursor is on it. Marks stay when the row is marked.</summary>
    public event EventHandler<int>? RowContextRequested;

    /// <summary>A key the list handled (it may have moved the cursor or the marks).</summary>
    public event EventHandler<FileListKey>? KeyHandled;

    /// <summary>
    /// Enter outside a rename. With no subscriber the key stays unhandled; with one, the host
    /// opens the entry under the cursor.
    /// </summary>
    public event EventHandler? EnterPressed;

    /// <summary>The host can refuse a key (inactive side, Ctrl or Super held): false leaves it unhandled.</summary>
    public Func<KeyEventArgs, bool>? AcceptKey { get; set; }

    /// <summary>Rows that exist as controls right now: the viewport and a small buffer.</summary>
    public int RealizedCount => Rows.GetRealizedContainers().Count();

    public bool IsRealized(int index) => RowViewAt(index) is not null;

    public string? HeaderText(string key)
    {
        foreach (var block in Header.Children.OfType<TextBlock>())
        {
            if (block.Tag is string header && header == key)
            {
                return block.Text;
            }
        }

        return null;
    }

    /// <summary>The laid-out width of each header column, left to right.</summary>
    public IReadOnlyList<double> HeaderColumnWidths() => Header.ColumnDefinitions.Select(c => c.ActualWidth).ToList();

    /// <summary>The laid-out width of each cell column of a realized row, or empty when it is not realized.</summary>
    public IReadOnlyList<double> RowColumnWidths(int index) =>
        RowViewAt(index)?.ColumnDefinitions.Select(c => c.ActualWidth).ToList() ?? [];

    public double HeaderHeight => Header.Bounds.Height;

    public double HeaderWidth => Header.Bounds.Width;

    /// <summary>The realized list item (row container) height, or null when the row is not realized.</summary>
    public double? RowContainerHeight(int index) => Rows.ContainerFromIndex(index)?.Bounds.Height;

    /// <summary>The foreground of the row's first cell (the name), or null when the row is not realized.</summary>
    public IBrush? NameForeground(int index) =>
        RowViewAt(index)?.GetVisualChildren().OfType<TextBlock>().FirstOrDefault()?.Foreground;

    public bool RowHasClass(int index, string name) => RowViewAt(index)?.Classes.Contains(name) ?? false;

    public double RowOpacity(int index) => RowViewAt(index)?.Opacity ?? 1;

    public IReadOnlyList<string> RenderedCells(int index) => RowViewAt(index)?.RenderedCells() ?? [];

    /// <summary>The row's rename editor, once the row is realized and being edited.</summary>
    public TextBox? EditorAt(int index) => RowViewAt(index)?.Editor;

    /// <summary>Window coordinates of a point on the open part of a realized row, past a short name.</summary>
    public Point? RowPoint(int index, TopLevel root)
    {
        var view = RowViewAt(index);
        if (view is null)
        {
            return null;
        }

        var at = view.TranslatePoint(new Point(Math.Max(0, view.Bounds.Width - 8), view.Bounds.Height / 2), root);
        return at;
    }

    internal FileListRowView? RowViewAt(int index)
    {
        if (index < 0 || Rows.ContainerFromIndex(index) is not { } container)
        {
            return null;
        }

        return container.GetVisualDescendants().OfType<FileListRowView>().FirstOrDefault();
    }

    private void OnTableChanged(object? sender, EventArgs e) => ShowTable();

    private void ShowTable()
    {
        if (_list.ThumbsActive)
        {
            ShowTiles();
            return;
        }

        Tiles.IsVisible = false;
        Tiles.ItemsSource = null;
        Header.IsVisible = true;
        Rows.IsVisible = true;
        BuildHeader();
        Rows.ItemsSource = _list.Items;
    }

    private void OnCursorRevealed(object? sender, int index)
    {
        if (index < 0 || index >= _list.Items.Count)
        {
            return;
        }

        if (_list.ThumbsActive)
        {
            Tiles.ScrollIntoView(index / _list.ThumbColumns);
            return;
        }

        Rows.ScrollIntoView(index);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var index = IndexOf(e.Source as Visual);
        if (point.Properties.IsRightButtonPressed && index >= 0)
        {
            Focus();
            if (!_list.IsRenaming)
            {
                _list.CursorIndex = index;
                RowContextRequested?.Invoke(this, index);
            }

            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || index < 0)
        {
            return;
        }

        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>() is not null && _list.IsRenaming)
        {
            return;
        }

        Focus();
        _list.PointerDown(index, MapPointer(e.KeyModifiers));
        if (e.ClickCount == 2)
        {
            RowActivated?.Invoke(this, index);
        }

        e.Handled = true;
    }

    private int IndexOf(Visual? source)
    {
        var tile = source?.FindAncestorOfType<FileListTileView>(true);
        if (tile is not null)
        {
            return tile.Index;
        }

        var item = source?.FindAncestorOfType<ListBoxItem>(true);
        return item is null ? -1 : Rows.IndexFromContainer(item);
    }

    private static FileListModifiers MapPointer(KeyModifiers keys)
    {
        var modifiers = FileListModifiers.None;
        if (keys.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= FileListModifiers.Shift;
        }

        if (keys.HasFlag(KeyModifiers.Control) || keys.HasFlag(KeyModifiers.Meta))
        {
            modifiers |= FileListModifiers.Toggle;
        }

        return modifiers;
    }

    /// <summary>Maps a key event to the list's key map. Hosts that own the focus forward their keys here.</summary>
    public void HandleKey(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Handled || MapKey(e.Key, _list.ThumbsActive) is not FileListKey key || AcceptKey?.Invoke(e) == false)
        {
            return;
        }

        if (_list.IsRenaming && key != FileListKey.Enter && key != FileListKey.Escape)
        {
            return;
        }

        var modifiers = FileListModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= FileListModifiers.Shift;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            modifiers |= FileListModifiers.Command;
        }

        if (_list.Key(key, modifiers))
        {
            e.Handled = true;
            KeyHandled?.Invoke(this, key);
        }
        else if (key == FileListKey.Enter && !_list.IsRenaming && EnterPressed is { } enter)
        {
            e.Handled = true;
            enter(this, EventArgs.Empty);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e) => HandleKey(e);

    private static FileListKey? MapKey(Key key, bool tiles)
    {
        if (tiles && key == Key.Left)
        {
            return FileListKey.Left;
        }

        if (tiles && key == Key.Right)
        {
            return FileListKey.Right;
        }

        switch (key)
        {
            case Key.PageDown: return FileListKey.PageDown;
            case Key.PageUp: return FileListKey.PageUp;
            case Key.Down: return FileListKey.Down;
            case Key.Up: return FileListKey.Up;
            case Key.End: return FileListKey.End;
            case Key.Home: return FileListKey.Home;
            case Key.Insert: return FileListKey.Insert;
            case Key.Space: return FileListKey.Space;
            case Key.Enter: return FileListKey.Enter;
            case Key.Escape: return FileListKey.Escape;
            default: return null;
        }
    }
}
