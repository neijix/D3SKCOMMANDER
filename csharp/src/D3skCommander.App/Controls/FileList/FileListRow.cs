using System.ComponentModel;
using System.Runtime.CompilerServices;
using D3skCommander.Core.FileKinds;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Controls.FileList;

/// <summary>One virtualized row. Panels pass an <see cref="Entry"/>; other tables pass their own cells.</summary>
public sealed class FileListRow : INotifyPropertyChanged
{
    private Func<string[]>? _makeCells;
    private string[]? _cells;
    private FileKind? _kind;
    private bool _kindKnown;
    private bool _isMarked;
    private bool _isCursor;
    private bool _isEditing;

    public FileListRow(
        string key,
        IReadOnlyList<string> cells,
        string editName,
        bool canMark,
        bool canRename,
        bool isDirectory,
        object? source)
        : this(key, editName, canMark, canRename, isDirectory, source)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _cells = cells.ToArray();
    }

    /// <summary>A row whose cells are made the first time it is drawn: a 100k-entry folder only formats what shows.</summary>
    internal FileListRow(
        string key,
        Func<string[]> cells,
        string editName,
        bool canMark,
        bool canRename,
        bool isDirectory,
        object? source)
        : this(key, editName, canMark, canRename, isDirectory, source)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _makeCells = cells;
    }

    private FileListRow(string key, string editName, bool canMark, bool canRename, bool isDirectory, object? source)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(editName);
        Key = key;
        EditName = editName;
        EditBuffer = editName;
        CanMark = canMark;
        CanRename = canRename;
        IsDirectory = isDirectory;
        Source = source;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public IReadOnlyList<string> Cells
    {
        get
        {
            if (_cells is null)
            {
                _cells = _makeCells!();
                _makeCells = null;
            }

            return _cells;
        }
    }

    public string EditName { get; }

    public string EditBuffer { get; set; }

    public bool CanMark { get; }

    public bool CanRename { get; }

    /// <summary>A real directory, not the parent row. Space marks it without moving.</summary>
    public bool IsDirectory { get; }

    public object? Source { get; }

    /// <summary>What the icon shows; null for a row that is not a file entry (no icon).</summary>
    public FileKind? Kind
    {
        get
        {
            if (!_kindKnown)
            {
                _kind = Source is Entry entry ? FileKindClassifier.Of(entry) : null;
                _kindKnown = true;
            }

            return _kind;
        }
    }

    public bool IsParent => Source is Entry entry && entry.IsParent;

    public bool IsLink => Source is Entry entry && entry.IsLink;

    public bool IsHidden => Source is Entry entry && entry.Hidden;

    public bool IsReadOnly => Source is Entry entry && entry.ReadOnly;

    public bool IsMarked
    {
        get => _isMarked;
        set => Set(ref _isMarked, value);
    }

    public bool IsCursor
    {
        get => _isCursor;
        set => Set(ref _isCursor, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => Set(ref _isEditing, value);
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
