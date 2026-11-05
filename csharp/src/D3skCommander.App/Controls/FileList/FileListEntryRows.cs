using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Controls.FileList;

/// <summary>Maps an <see cref="Entry"/> onto the full or brief columns.</summary>
public static class FileListEntryRows
{
    public const double ExtensionWidth = 72;
    public const double SizeWidth = 96;
    public const double DateWidth = 148;
    public const double AttributesWidth = 96;

    public static IReadOnlyList<FileListColumn> Columns(FileListView view, FileListColumnFlags shown)
    {
        var columns = new List<FileListColumn> { new("name", 1, true) };
        if (view != FileListView.Full)
        {
            return columns;
        }

        AddOptional(columns, shown);
        return columns;
    }

    public static FileListRow Create(Entry entry, FileListView view, FileListColumnFlags shown, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(text);
        var columns = Columns(view, shown);
        var cells = new string[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            cells[i] = Cell(entry, columns[i].HeaderKey, view, shown, text);
        }

        var parent = entry.IsParent;
        return new FileListRow(
            entry.Path,
            cells,
            parent ? text.Get("filelist.parent") : entry.Name,
            !parent,
            !parent,
            entry.IsDirectory && !parent,
            entry);
    }

    private static void AddOptional(List<FileListColumn> columns, FileListColumnFlags shown)
    {
        if (shown.HasFlag(FileListColumnFlags.Extension))
        {
            columns.Add(new FileListColumn("ext", ExtensionWidth));
        }

        if (shown.HasFlag(FileListColumnFlags.Size))
        {
            columns.Add(new FileListColumn("size", SizeWidth));
        }

        if (shown.HasFlag(FileListColumnFlags.Date))
        {
            columns.Add(new FileListColumn("date", DateWidth));
        }

        if (shown.HasFlag(FileListColumnFlags.Attributes))
        {
            columns.Add(new FileListColumn("attr", AttributesWidth));
        }
    }

    private static string Cell(Entry entry, string key, FileListView view, FileListColumnFlags shown, ILocalizer text)
    {
        if (key == "name")
        {
            return NameOf(entry, view, shown, text);
        }

        if (key == "ext")
        {
            return ExtensionOf(entry);
        }

        if (key == "size")
        {
            return SizeOf(entry, text);
        }

        if (key == "date")
        {
            return DateOf(entry);
        }

        if (key == "attr")
        {
            return entry.Attributes;
        }

        return "";
    }

    private static string NameOf(Entry entry, FileListView view, FileListColumnFlags shown, ILocalizer text)
    {
        if (entry.IsParent)
        {
            return text.Get("filelist.parent");
        }

        if (view != FileListView.Full || entry.IsDirectory || !shown.HasFlag(FileListColumnFlags.Extension))
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

        if (entry.IsDirectory && entry.Size == 0)
        {
            return text.Get("filelist.directory");
        }

        return LocalFileSystem.FormatGrouped(entry.Size);
    }

    private static string DateOf(Entry entry)
    {
        if (entry.IsParent)
        {
            return "";
        }

        return LocalFileSystem.FormatTime(entry.Modified);
    }
}
