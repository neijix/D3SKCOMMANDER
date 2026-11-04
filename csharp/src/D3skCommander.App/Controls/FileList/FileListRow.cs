using System.ComponentModel;
using System.Runtime.CompilerServices;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Controls.FileList;

/// <summary>One virtualized row. Panels pass an <see cref="Entry"/>; other tables pass their own cells.</summary>
public sealed class FileListRow : INotifyPropertyChanged
{
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
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(editName);
        Key = key;
        Cells = cells.ToArray();
        EditName = editName;
        EditBuffer = editName;
        CanMark = canMark;
        CanRename = canRename;
        IsDirectory = isDirectory;
        Source = source;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public IReadOnlyList<string> Cells { get; }

    public string EditName { get; }

    public string EditBuffer { get; set; }

    public bool CanMark { get; }

    public bool CanRename { get; }

    /// <summary>A real directory, not the parent row. Space marks it without moving.</summary>
    public bool IsDirectory { get; }

    public object? Source { get; }

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
