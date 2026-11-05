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
        InitializeComponent();
        Rows.ItemTemplate = new FuncDataTemplate<FileListRow>((_, _) => new FileListRowView(), true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Rows.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _list.TableChanged += OnTableChanged;
        _list.CursorRevealed += OnCursorRevealed;
        ShowTable();
    }

    /// <summary>The rules behind the control: entries, view, marks, cursor and rename.</summary>
    public FileList List => _list;

    /// <summary>A double-click landed on a row (the row index).</summary>
    public event EventHandler<int>? RowActivated;

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
        BuildHeader();
        Rows.ItemsSource = _list.Items;
    }

    private void OnCursorRevealed(object? sender, int index)
    {
        if (index >= 0 && index < _list.Items.Count)
        {
            Rows.ScrollIntoView(index);
        }
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
                Margin = new Thickness(4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(block, i);
            Header.Children.Add(block);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var index = IndexOf(e.Source as Visual);
        if (index < 0)
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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var key = MapKey(e.Key);
        if (key is null)
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

        if (_list.Key(key.Value, modifiers))
        {
            e.Handled = true;
        }
    }

    private static FileListKey? MapKey(Key key) => key switch
    {
        Key.PageDown => FileListKey.PageDown,
        Key.PageUp => FileListKey.PageUp,
        Key.Down => FileListKey.Down,
        Key.Up => FileListKey.Up,
        Key.End => FileListKey.End,
        Key.Home => FileListKey.Home,
        Key.Insert => FileListKey.Insert,
        Key.Space => FileListKey.Space,
        Key.Enter => FileListKey.Enter,
        Key.Escape => FileListKey.Escape,
        _ => null,
    };
}
