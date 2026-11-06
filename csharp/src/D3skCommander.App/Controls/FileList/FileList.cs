using System.Collections.Specialized;
using D3skCommander.App.Shell;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Controls.FileList;

/// <summary>One virtualized file list for the full and brief views, and for the other tables.</summary>
public sealed partial class FileList : IShellWidget
{
    private readonly HashSet<string> _marks = new(StringComparer.Ordinal);
    private readonly object _rows = new();
    private List<FileListRow> _rowList = [];
    private Dictionary<string, int>? _keyIndex;
    private List<FileListColumn> _columns = [];
    private IEnumerable<Entry>? _entries;
    private IEnumerable<Entry>? _watched;
    private ILocalizer _localizer;
    private int _cursorIndex = -1;
    private int _scroll;
    private int _scrollRequest = -1;
    private int _activated = -1;
    private readonly Dictionary<int, float> _rowTops = new();
    private bool _custom;
    private int _thumbColumns = 4;

    public FileList()
        : this(Localizer.Load())
    {
    }

    public FileList(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
        _columns = FileListEntryRows.Columns(View, _layout).ToList();
    }

    public string? Name => "Rows";

    /// <summary>Stable identity of the one list. Full and brief views share it.</summary>
    public object Rows => _rows;

    public IEnumerable<Entry>? Entries
    {
        get => _entries;
        set
        {
            _custom = false;
            _entries = value;
            Watch(value);
            RebuildFromEntries();
        }
    }

    private FileListView _view = FileListView.Full;
    private IReadOnlyList<ColumnSetColumn> _layout = FileListEntryRows.ClassicLayout;

    public FileListView View
    {
        get => _view;
        set => ChangeView(value);
    }

    /// <summary>The optional columns of the full view, in order, with their widths (0 = automatic).</summary>
    public IReadOnlyList<ColumnSetColumn> Layout
    {
        get => _layout;
        set => ChangeLayout(value);
    }

    /// <summary>Tiles per row in the thumbnail view (the control sets it from its width).</summary>
    internal int ThumbColumns
    {
        get => _thumbColumns;
        set => _thumbColumns = Math.Max(1, value);
    }

    /// <summary>The tile layout is on: thumbnail view over entries (custom tables stay tables).</summary>
    internal bool ThumbsActive => _view == FileListView.Thumbs && !_custom;

    /// <summary>Panel entries carry a file-kind icon before the name; custom tables do not.</summary>
    internal bool ShowsIcons => !_custom;

    public double ViewportHeight { get; set; } = 360;

    public int CursorIndex
    {
        get => _cursorIndex;
        set => PlaceCursor(value, true);
    }

    public int MarkedCount => _marks.Count;

    public Avalonia.Controls.Control CreateContent() => new FileListControl(this);

    public bool IsRenaming => Editing is not null;

    public int EditorRow => IsRenaming ? _cursorIndex : -1;

    public string RenameText
    {
        get => Editing?.EditBuffer ?? "";
        set
        {
            if (Editing is not null)
            {
                Editing.EditBuffer = value;
            }
        }
    }

    internal IReadOnlyList<FileListColumn> CurrentColumns => _columns;

    internal IReadOnlyList<FileListRow> Items => _rowList;

    internal FileListRow? Editing { get; private set; }

    /// <summary>The rows, the columns or the header texts changed: a view shows the new table.</summary>
    internal event EventHandler? TableChanged;

    /// <summary>The cursor moved and wants to be on screen (the row index).</summary>
    internal event EventHandler<int>? CursorRevealed;

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
        foreach (var column in _columns)
        {
            if (column.HeaderKey == key)
            {
                return _localizer.Get(key);
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
        if (!IsRealized(index))
        {
            return false;
        }

        var row = _rowList[index];
        return name switch
        {
            "marked" => row.IsMarked,
            "cursor" => row.IsCursor,
            "parent" => row.IsParent,
            "directory" => row.IsDirectory,
            "link" => row.IsLink,
            "hidden" => row.IsHidden,
            "readonly" => row.IsReadOnly,
            _ => false,
        };
    }

    public double RowOpacity(int index)
    {
        if (!IsRealized(index))
        {
            return 1;
        }

        return _rowList[index].IsHidden ? 0.5 : 1;
    }

    public IReadOnlyList<string> RenderedCells(int index)
    {
        if (!IsRealized(index))
        {
            return [];
        }

        return _rowList[index].Cells;
    }

    public int RealizedCount
    {
        get
        {
            if (_rowList.Count == 0)
            {
                return 0;
            }

            return Math.Min(_rowList.Count - _scroll, VisibleRows());
        }
    }

    public bool IsRealized(int index) =>
        index >= _scroll && index < _scroll + VisibleRows() && index < _rowList.Count && index >= 0;

    /// <summary>The second press of a double-click landed on a row this frame.</summary>
    public bool TakeActivation()
    {
        var open = _activated >= 0;
        _activated = -1;
        return open;
    }

    /// <summary>Screen Y of a row drawn this frame. A row the clipper skipped is absent.</summary>
    public bool TryRowTop(int index, out float top) => _rowTops.TryGetValue(index, out top);

    public void Use(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
        if (_custom)
        {
            TableChanged?.Invoke(this, EventArgs.Empty);
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
        _keyIndex = null;
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
        TableChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TypeRename(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Editing is not null)
        {
            Editing.EditBuffer += text;
        }
    }

    internal void RaiseRename(FileListRow row, string name) =>
        RenameCommitted?.Invoke(this, new FileListRenameEventArgs(row, name));

    internal void RaiseDirectoryMarked(FileListRow row) => DirectoryMarked?.Invoke(this, row);

    internal int VisibleRows()
    {
        var body = Math.Max(0, ViewportHeight - FileListLayout.HeaderHeight);
        var rows = (int)Math.Floor(body / FileListLayout.RowHeight);
        return Math.Max(1, rows);
    }

    private void ChangeView(FileListView view)
    {
        if (_view == view)
        {
            return;
        }

        _view = view;
        if (!_custom)
        {
            RebuildFromEntries();
        }
    }

    private void ChangeLayout(IReadOnlyList<ColumnSetColumn> layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (_layout.Select(Sig).SequenceEqual(layout.Select(Sig)))
        {
            return;
        }

        _layout = layout;
        if (!_custom)
        {
            RebuildFromEntries();
        }
    }

    private static string Sig(ColumnSetColumn column) => $"{(int)column.Id}:{column.Width}";

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
        _columns = FileListEntryRows.Columns(View, _layout).ToList();
        var built = _entries is IReadOnlyCollection<Entry> known ? new List<FileListRow>(known.Count) : [];
        if (_entries is not null)
        {
            foreach (var entry in _entries)
            {
                built.Add(FileListEntryRows.Create(entry, View, _columns, _localizer));
            }
        }

        _rowList = built;
        _keyIndex = null;
        KeepMarks();
        var preferred = IndexOfKey(keep);
        Publish(preferred < 0 ? previous : preferred);
        TableChanged?.Invoke(this, EventArgs.Empty);
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
        var next = NextCursor(preferred);
        _cursorIndex = next;
        if (next >= 0)
        {
            _rowList[next].IsCursor = true;
            ScrollTo(next);
            CursorRevealed?.Invoke(this, next);
        }
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

    private string? CurrentKey()
    {
        if (_cursorIndex < 0 || _cursorIndex >= _rowList.Count)
        {
            return null;
        }

        return _rowList[_cursorIndex].Key;
    }

    // The first row with that key, from an index built on first use after the rows change.
    private int IndexOfKey(string? key)
    {
        if (key is null)
        {
            return -1;
        }

        if (_keyIndex is null)
        {
            _keyIndex = new Dictionary<string, int>(_rowList.Count, StringComparer.Ordinal);
            for (var i = 0; i < _rowList.Count; i++)
            {
                _keyIndex.TryAdd(_rowList[i].Key, i);
            }
        }

        return _keyIndex.TryGetValue(key, out var index) ? index : -1;
    }

    private void ScrollTo(int index)
    {
        var before = _scroll;
        var visible = VisibleRows();
        if (index < _scroll)
        {
            _scroll = index;
        }
        else if (index >= _scroll + visible)
        {
            _scroll = index - visible + 1;
        }

        var max = Math.Max(0, _rowList.Count - visible);
        if (_scroll > max)
        {
            _scroll = max;
        }

        if (_scroll < 0)
        {
            _scroll = 0;
        }

        if (_scroll != before)
        {
            _scrollRequest = _scroll;
        }
    }
}
