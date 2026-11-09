using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Controls.FileList;

/// <summary>Maps an <see cref="Entry"/> onto the full or brief columns.</summary>
public static class FileListEntryRows
{
    public const double ExtensionWidth = 72;
    public const double SizeWidth = 96;
    public const double DateWidth = 148;
    public const double AttributesWidth = 96;

    /// <summary>The columns every optional column set starts from: the four classic ones, auto width.</summary>
    public static IReadOnlyList<ColumnSetColumn> ClassicLayout { get; } =
    [
        new(ColumnId.Extension),
        new(ColumnId.Size),
        new(ColumnId.Modified),
        new(ColumnId.Attributes),
    ];

    public static IReadOnlyList<FileListColumn> Columns(FileListView view, IReadOnlyList<ColumnSetColumn> layout)
    {
        var columns = new List<FileListColumn> { new("name", 1, true) };
        if (view != FileListView.Full)
        {
            return columns;
        }

        foreach (var column in layout)
        {
            var width = column.Width > 0 ? Math.Max(column.Width, ColumnInfo.MinWidth(column.Id)) : ColumnInfo.AutoWidth(column.Id);
            columns.Add(new FileListColumn(ColumnInfo.HeaderKey(column.Id), width, false, column.Id));
        }

        return columns;
    }

    /// <summary>The row for <paramref name="entry"/> under <paramref name="columns"/> (from <see cref="Columns"/>); its cells are formatted when first read.</summary>
    public static FileListRow Create(
        Entry entry,
        FileListView view,
        IReadOnlyList<FileListColumn> columns,
        ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(text);
        var parent = entry.IsParent;
        return new FileListRow(
            entry.Path,
            () => Cells(entry, view, columns, text),
            parent ? text.Get("filelist.parent") : entry.Name,
            !parent,
            !parent,
            entry.IsDirectory && !parent,
            entry);
    }

    private static string[] Cells(
        Entry entry,
        FileListView view,
        IReadOnlyList<FileListColumn> columns,
        ILocalizer text)
    {
        var showsExtension = columns.Any(column => column.Id == ColumnId.Extension);
        var cells = new string[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            cells[i] = Cell(entry, columns[i].HeaderKey, view, showsExtension, text);
        }

        return cells;
    }

    private static string Cell(Entry entry, string key, FileListView view, bool showsExtension, ILocalizer text)
    {
        var id = ColumnInfo.FromHeaderKey(key);
        return id switch
        {
            ColumnId.Name => NameOf(entry, view, showsExtension, text),
            ColumnId.Extension => ExtensionOf(entry),
            ColumnId.Size => SizeOf(entry, text),
            ColumnId.Modified => DateOf(entry),
            ColumnId.Attributes => entry.Attributes,
            ColumnId.Created => entry.IsParent ? "" : ColumnValues.Created(entry),
            ColumnId.Accessed => entry.IsParent ? "" : ColumnValues.Accessed(entry),
            ColumnId.Owner => entry.IsParent ? "" : ColumnValues.Owner(entry),
            ColumnId.Group => entry.IsParent ? "" : ColumnValues.Group(entry),
            ColumnId.Kind => ColumnValues.Kind(entry, text),
            _ => "",
        };
    }

    private static string NameOf(Entry entry, FileListView view, bool showsExtension, ILocalizer text)
    {
        if (entry.IsParent)
        {
            return text.Get("filelist.parent");
        }

        if (view != FileListView.Full || entry.IsDirectory || !showsExtension)
        {
            return entry.Name;
        }

        return entry.Stem;
    }

    private static string ExtensionOf(Entry entry)
    {
        if (entry.IsParent || entry.IsDirectory)
        {
            return "";
        }

        return entry.Extension;
    }

    private static string SizeOf(Entry entry, ILocalizer text)
    {
        if (entry.IsParent)
        {
            return "";
        }

        if (entry.SizeText is { } shown)
        {
            return shown;
        }

        if (entry.IsDirectory && entry.Size == 0)
        {
            return text.Get("filelist.directory");
        }

        return LocalFileSystem.FormatSize(entry.Size);
    }

    private static string DateOf(Entry entry)
    {
        if (entry.IsParent)
        {
            return "";
        }

        return entry.FillText ?? LocalFileSystem.FormatTime(entry.Modified);
    }
}
