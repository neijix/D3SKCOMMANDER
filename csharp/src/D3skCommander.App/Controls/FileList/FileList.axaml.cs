using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Controls.FileList;

/// <summary>One virtualized file list for the full and brief views, and for the other tables.</summary>
public partial class FileList : UserControl
{
    public static readonly StyledProperty<IEnumerable<Entry>?> EntriesProperty =
        AvaloniaProperty.Register<FileList, IEnumerable<Entry>?>(nameof(Entries));

    public static readonly StyledProperty<FileListView> ViewProperty =
        AvaloniaProperty.Register<FileList, FileListView>(nameof(View), FileListView.Full);

    public static readonly StyledProperty<FileListColumnFlags> ShownColumnsProperty =
        AvaloniaProperty.Register<FileList, FileListColumnFlags>(nameof(ShownColumns), FileListColumnFlags.All);

    public static readonly DirectProperty<FileList, int> CursorIndexProperty =
        AvaloniaProperty.RegisterDirect<FileList, int>(nameof(CursorIndex), o => o.CursorIndex, (o, v) => o.CursorIndex = v);

    private readonly HashSet<string> _marks = new(StringComparer.Ordinal);
    private List<FileListRow> _rowList = [];
    private List<FileListColumn> _columns = [];
    private IEnumerable<Entry>? _watched;
    private ILocalizer _localizer;
    private int _cursorIndex = -1;
    private bool _custom;

    public FileList()
        : this(Localizer.Load())
    {
    }

    public FileList(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
        InitializeComponent();
        Rows.ItemTemplate = new FuncDataTemplate<FileListRow>((_, _) => new FileListRowView(), true);
        Rows.SelectionMode = SelectionMode.Single;
        AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        Rows.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _columns = FileListEntryRows.Columns(View, ShownColumns).ToList();
        BuildHeader();
    }

    public IEnumerable<Entry>? Entries
    {
        get => GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public FileListView View
    {
        get => GetValue(ViewProperty);
        set => SetValue(ViewProperty, value);
    }

    public FileListColumnFlags ShownColumns
    {
        get => GetValue(ShownColumnsProperty);
        set => SetValue(ShownColumnsProperty, value);
    }

    public int CursorIndex
    {
        get => _cursorIndex;
        set => PlaceCursor(value, true);
    }

    public int MarkedCount => _marks.Count;

    public bool IsRenaming => Editing is not null;

    internal IReadOnlyList<FileListColumn> CurrentColumns => _columns;

    internal FileListRow? Editing { get; private set; }

    public event EventHandler<FileListRenameEventArgs>? RenameCommitted;

    /// <summary>Space marked an unmarked directory. The panel computes its size.</summary>
    public event EventHandler<FileListRow>? DirectoryMarked;

    public bool IsMarked(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _marks.Contains(key);
    }

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

    public int ColumnIndex(string key)
    {
        for (var i = 0; i < _columns.Count; i++)
        {
            if (_columns[i].HeaderKey == key)
            {
                return i;
            }
        }

        return -1;
    }

    public string? CellText(int index, int column)
    {
        if (index < 0 || index >= _rowList.Count)
        {
            return null;
        }

        var cells = _rowList[index].Cells;
        if (column < 0 || column >= cells.Count)
        {
            return null;
        }

        return cells[column];
    }

    public bool RowHasClass(int index, string name)
    {
        var view = RowViewAt(index);
        return view is not null && view.Classes.Contains(name);
    }

    public double RowOpacity(int index) => RowViewAt(index)?.Opacity ?? 1;

    public IReadOnlyList<string> RenderedCells(int index)
    {
        var view = RowViewAt(index);
        return view is null ? [] : view.RenderedCells();
    }

    public void Use(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
        if (_custom)
        {
            BuildHeader();
            return;
        }

        RebuildFromEntries();
    }

    public void ShowRows(IReadOnlyList<FileListColumn> columns, IEnumerable<FileListRow> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        _custom = true;
        Watch(null);
        CloseEditor();
        _columns = columns.ToList();
        _rowList = rows.ToList();
        _marks.Clear();
        foreach (var row in _rowList)
        {
            if (row.IsMarked && row.CanMark)
            {
                _marks.Add(row.Key);
            }
            else
            {
                row.IsMarked = false;
            }
        }

        Publish(0);
    }

    internal void RaiseRename(FileListRow row, string name) =>
        RenameCommitted?.Invoke(this, new FileListRenameEventArgs(row, name));

    internal void RaiseDirectoryMarked(FileListRow row) => DirectoryMarked?.Invoke(this, row);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EntriesProperty)
        {
            _custom = false;
            Watch(change.NewValue as IEnumerable<Entry>);
            RebuildFromEntries();
            return;
        }

        if (_custom)
        {
            return;
        }

        if (change.Property == ViewProperty || change.Property == ShownColumnsProperty)
        {
            RebuildFromEntries();
        }
    }

    private void Watch(IEnumerable<Entry>? next)
    {
        if (ReferenceEquals(_watched, next))
        {
            return;
        }

        if (_watched is INotifyCollectionChanged old)
        {
            old.CollectionChanged -= OnEntriesChanged;
        }

        _watched = next;
        if (next is INotifyCollectionChanged fresh)
        {
            fresh.CollectionChanged += OnEntriesChanged;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_custom)
        {
            RebuildFromEntries();
        }
    }

    private void RebuildFromEntries()
    {
        var keep = CurrentKey();
        var previous = _cursorIndex;
        CloseEditor();
        _columns = FileListEntryRows.Columns(View, ShownColumns).ToList();
        var built = new List<FileListRow>();
        if (Entries is not null)
        {
            foreach (var entry in Entries)
            {
                built.Add(FileListEntryRows.Create(entry, View, ShownColumns, _localizer));
            }
        }

        _rowList = built;
        KeepMarks();
        var preferred = IndexOfKey(keep);
        Publish(preferred < 0 ? previous : preferred);
    }

    private void KeepMarks()
    {
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in _rowList)
        {
            var marked = row.CanMark && _marks.Contains(row.Key);
            row.IsMarked = marked;
            if (marked)
            {
                live.Add(row.Key);
            }
        }

        _marks.Clear();
        foreach (var key in live)
        {
            _marks.Add(key);
        }
    }

    private void Publish(int preferred)
    {
        Rows.ItemsSource = _rowList;
        BuildHeader();
        var next = NextCursor(preferred);
        if (_cursorIndex != next)
        {
            SetAndRaise(CursorIndexProperty, ref _cursorIndex, next);
        }

        if (next < 0)
        {
            Rows.SelectedIndex = -1;
            return;
        }

        _rowList[next].IsCursor = true;
        Rows.SelectedIndex = next;
        Rows.ScrollIntoView(_rowList[next]);
    }

    private int NextCursor(int preferred)
    {
        if (_rowList.Count == 0)
        {
            return -1;
        }

        if (preferred < 0)
        {
            return 0;
        }

        return Math.Min(preferred, _rowList.Count - 1);
    }

    private void BuildHeader()
    {
        Header.Children.Clear();
        Header.ColumnDefinitions.Clear();
        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            Header.ColumnDefinitions.Add(FileListLayout.Definition(column));
            var label = new TextBlock
            {
                Text = _localizer.Get(column.HeaderKey),
                Tag = column.HeaderKey,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(4, 0),
            };
            Grid.SetColumn(label, i);
            Header.Children.Add(label);
        }
    }

    private string? CurrentKey()
    {
        if (_cursorIndex < 0 || _cursorIndex >= _rowList.Count)
        {
            return null;
        }

        return _rowList[_cursorIndex].Key;
    }

    private int IndexOfKey(string? key)
    {
        if (key is null)
        {
            return -1;
        }

        for (var i = 0; i < _rowList.Count; i++)
        {
            if (_rowList[i].Key == key)
            {
                return i;
            }
        }

        return -1;
    }

    private FileListRowView? RowViewAt(int index)
    {
        if (Rows.ContainerFromIndex(index) is not Control container)
        {
            return null;
        }

        if (container is FileListRowView view)
        {
            return view;
        }

        return container.GetVisualDescendants().OfType<FileListRowView>().FirstOrDefault();
    }
}
