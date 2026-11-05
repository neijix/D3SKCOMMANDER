using System.Collections;
using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Viewer;

namespace D3skCommander.App.Features.Lister;

/// <summary>One row of the viewer list: the line number gutter (text mode) and the text.</summary>
public sealed record ViewerLine(string Gutter, string Text);

/// <summary>A mode the switch offers, with its localized label.</summary>
public sealed record ViewerModeChoice(ViewerMode Mode, string Label);

/// <summary>
/// The viewer tool window's state over Core's <see cref="FileViewer"/>. A file opens off the UI
/// thread: the name and a busy state show at once, then the file or its typed error. Text and
/// hex rows are built on demand, so a list of a million rows costs nothing until it scrolls.
/// </summary>
public sealed class ViewerViewModel : ObservableObject, IShellWidget
{
    private readonly ILocalizer _text;
    private readonly Func<string, FileViewer> _open;
    private FileViewer? _viewer;
    private int _ticket;
    private int _columns = 100;
    private string _name = "";
    private bool _isBusy;
    private string _error = "";
    private string _notice = "";
    private string _status = "";
    private string _truncated = "";
    private string _header = "";
    private IReadOnlyList<ViewerLine> _rows = [];
    private IReadOnlyList<ViewerModeChoice> _modes = [];
    private ViewerModeChoice? _selectedMode;
    private int _selectedRow = -1;
    private bool _wrap = true;
    private bool _matchCase;
    private string _findText = "";
    private string _filter = "";

    public ViewerViewModel(ILocalizer text)
        : this(text, FileViewer.Open)
    {
    }

    public ViewerViewModel(ILocalizer text, Func<string, FileViewer> open)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(open);
        _text = text;
        _open = open;
    }

    public string? Name => "Lister";

    public string FileName
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The typed error as localized text plus the path; empty when the file opened.</summary>
    public string ErrorMessage
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _error.Length > 0;

    public string Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    /// <summary>Encoding and size, or the table's note.</summary>
    public string StatusText
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Core's truncated notice, empty when the whole file is loaded.</summary>
    public string TruncatedText
    {
        get => _truncated;
        private set => SetProperty(ref _truncated, value);
    }

    /// <summary>Column titles above the rows in table mode.</summary>
    public string Header
    {
        get => _header;
        private set => SetProperty(ref _header, value);
    }

    public IReadOnlyList<ViewerLine> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public IReadOnlyList<ViewerModeChoice> Modes
    {
        get => _modes;
        private set => SetProperty(ref _modes, value);
    }

    public ViewerModeChoice? SelectedMode
    {
        get => _selectedMode;
        private set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(IsText));
                OnPropertyChanged(nameof(IsHex));
                OnPropertyChanged(nameof(IsTable));
                OnPropertyChanged(nameof(CanFind));
            }
        }
    }

    public bool IsText => _selectedMode?.Mode == ViewerMode.Text;

    public bool IsHex => _selectedMode?.Mode == ViewerMode.Hex;

    public bool IsTable => _selectedMode?.Mode == ViewerMode.Table;

    public bool CanFind => IsText || IsHex;

    /// <summary>The display row to highlight and keep in view; -1 for none.</summary>
    public int SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (SetProperty(ref _wrap, value) && _viewer is { } viewer)
            {
                viewer.Wrap = value;
                Reshow(viewer);
            }
        }
    }

    public bool MatchCase
    {
        get => _matchCase;
        set => SetProperty(ref _matchCase, value);
    }

    public string FindText
    {
        get => _findText;
        set => SetProperty(ref _findText, value ?? "");
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? "") && _viewer is { Mode: ViewerMode.Table } viewer)
            {
                viewer.SetFilter(_filter);
                Reshow(viewer);
            }
        }
    }

    public int? TextCursor => _viewer?.TextCursor;

    public int? HexCursor => _viewer?.HexCursor;

    public string WrapLabel => _text.Get("lister.wrap");

    public string MatchCaseLabel => _text.Get("lister.match_case");

    public string FindLabel => _text.Get("lister.find");

    public string NextLabel => _text.Get("lister.find_next");

    public string PreviousLabel => _text.Get("lister.find_prev");

    public string FilterLabel => _text.Get("lister.filter");

    /// <summary>The opened file's path, or empty.</summary>
    public string Path => _viewer?.Path ?? "";

    public void Draw()
    {
    }

    public Control CreateContent() => new ViewerView { DataContext = this };

    /// <summary>Sets the wrapping width in characters. A change rebuilds the wrapped text rows.</summary>
    public void SetColumns(int columns)
    {
        var next = Math.Max(10, columns);
        if (next == _columns)
        {
            return;
        }

        _columns = next;
        if (_viewer is { Mode: ViewerMode.Text, Wrap: true } viewer)
        {
            Reshow(viewer);
        }
    }

    /// <summary>Shows <paramref name="path"/>: the name and busy state at once, the file when the load ends.</summary>
    public async Task OpenAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var ticket = ++_ticket;
        Begin(path);
        var loaded = await Task.Run(() => Load(path)).ConfigureAwait(true);
        if (ticket == _ticket)
        {
            UiThread.Invoke(() => Show(loaded));
        }
    }

    public async Task SetModeAsync(ViewerMode mode)
    {
        if (_viewer is not { } viewer || viewer.Mode == mode || !viewer.Modes.Contains(mode))
        {
            return;
        }

        var ticket = ++_ticket;
        IsBusy = true;
        var loaded = await Task.Run(() =>
        {
            viewer.SetMode(mode);
            return new Loaded(viewer, Render(viewer));
        }).ConfigureAwait(true);
        if (ticket == _ticket)
        {
            UiThread.Invoke(() => Show(loaded));
        }
    }

    /// <summary>Finds the next or previous hit of <see cref="FindText"/> and selects its row.</summary>
    public bool Find(bool forward)
    {
        if (_viewer is not { } viewer || !CanFind)
        {
            return false;
        }

        viewer.FindText = _findText;
        viewer.MatchCase = _matchCase;
        var found = viewer.Find(forward);
        Notice = viewer.Notice == ViewerNotice.NotFound ? _text.Get("lister.not_found") : "";
        if (found && viewer.TakeScrollRow() is int row)
        {
            SelectedRow = row;
        }

        OnPropertyChanged(nameof(TextCursor));
        OnPropertyChanged(nameof(HexCursor));
        return found;
    }

    /// <summary>Selects the last loaded row.</summary>
    public void GoToEnd() => SelectedRow = Rows.Count - 1;

    private void Begin(string path)
    {
        FileName = System.IO.Path.GetFileName(path);
        IsBusy = true;
        ErrorMessage = "";
        Notice = _text.Get("lister.opening").Replace("{name}", FileName, StringComparison.Ordinal);
        Rows = [];
        Header = "";
        SelectedRow = -1;
    }

    private Loaded Load(string path)
    {
        var viewer = _open(path);
        if (viewer.Error is null && viewer.Mode == ViewerMode.Image)
        {
            // Image mode is a later row; until then an image file opens as bytes.
            viewer.SetMode(ViewerMode.Hex);
        }

        viewer.Wrap = _wrap;
        return new Loaded(viewer, viewer.Error is null ? Render(viewer) : []);
    }

    private void Show(Loaded loaded)
    {
        var viewer = loaded.Viewer;
        _viewer = viewer;
        IsBusy = false;
        Notice = "";
        SelectedRow = -1;
        if (viewer.Error is { } error)
        {
            ErrorMessage = _text.Get(ErrorText.Key(error.Kind)) + ": " + (error.Path ?? viewer.Path);
            Modes = [];
            SelectedMode = null;
            Rows = [];
            StatusText = "";
            TruncatedText = "";
            return;
        }

        ErrorMessage = "";
        var choices = new List<ViewerModeChoice>();
        foreach (var mode in viewer.Modes.Where(mode => mode != ViewerMode.Image))
        {
            choices.Add(new ViewerModeChoice(mode, _text.Get("lister.mode." + mode.ToString().ToLowerInvariant())));
        }

        Modes = choices;
        SelectedMode = choices.Find(choice => choice.Mode == viewer.Mode);
        _filter = viewer.Filter;
        OnPropertyChanged(nameof(Filter));
        Display(viewer, loaded.Rows);
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(TextCursor));
        OnPropertyChanged(nameof(HexCursor));
    }

    private void Display(FileViewer viewer, IReadOnlyList<ViewerLine> rows)
    {
        Rows = rows;
        Header = viewer.Table is { } table && viewer.Mode == ViewerMode.Table ? ViewerRowText.Header(table) : "";
        if (viewer.Mode == ViewerMode.Table)
        {
            StatusText = viewer.Table?.Note ?? "";
            TruncatedText = "";
            return;
        }

        StatusText = viewer.EncodingName + " | " + LocalFileSystem.FormatSize(viewer.Size);
        TruncatedText = viewer.Truncated ? _text.Get("lister.truncated") : "";
    }

    private void Reshow(FileViewer viewer)
    {
        SelectedRow = -1;
        Display(viewer, Render(viewer));
    }

    private IReadOnlyList<ViewerLine> Render(FileViewer viewer)
    {
        switch (viewer.Mode)
        {
            case ViewerMode.Text:
                var rows = viewer.TextRows(_columns);
                return new LazyRows(rows.Count, i => new ViewerLine(Gutter(rows[i]), rows[i].Text));
            case ViewerMode.Hex:
                return new LazyRows(viewer.HexRowCount, i => new ViewerLine("", viewer.HexLine(i)));
            case ViewerMode.Table when viewer.Table is { } table:
                var shown = viewer.FilteredRows;
                return new LazyRows(shown.Count, i => new ViewerLine("", ViewerRowText.Row(table, shown[i])));
            default:
                return [];
        }
    }

    private static string Gutter(ViewerTextRow row) =>
        row.Number is int number ? number.ToString(CultureInfo.InvariantCulture).PadLeft(6) + "  " : new string(' ', 8);

    private sealed record Loaded(FileViewer Viewer, IReadOnlyList<ViewerLine> Rows);
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
