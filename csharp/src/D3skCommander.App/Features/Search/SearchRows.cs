using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Search;

namespace D3skCommander.App.Features.Search;

/// <summary>Result rows: name, the folder holding it, size, date, and the matching line of a content search.</summary>
internal static class SearchRows
{
    public static IReadOnlyList<FileListColumn> Columns { get; } =
    [
        new FileListColumn("name", 1, true),
        new FileListColumn("search.col.folder", 200),
        new FileListColumn("size", FileListEntryRows.SizeWidth),
        new FileListColumn("date", FileListEntryRows.DateWidth),
        new FileListColumn("search.col.match", 180),
    ];

    /// <summary>Duplicates: name, group number, folder, size, date. Rows come in group order.</summary>
    public static IReadOnlyList<FileListColumn> DuplicateColumns { get; } =
    [
        new FileListColumn("name", 1, true),
        new FileListColumn("search.col.group", 70),
        new FileListColumn("search.col.folder", 200),
        new FileListColumn("size", FileListEntryRows.SizeWidth),
        new FileListColumn("date", FileListEntryRows.DateWidth),
    ];

    public static FileListRow Row(SearchHit hit, ILocalizer text, bool duplicates = false)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(text);
        var size = hit.IsDirectory ? text.Get("folder") : LocalFileSystem.FormatSize(hit.Size);
        var match = hit.Line is int line ? line + ": " + hit.LineText : "";
        var cells = duplicates
            ? new[] { hit.Name, hit.Group.ToString(System.Globalization.CultureInfo.InvariantCulture), ParentOf(hit.Path), size, LocalFileSystem.FormatTime(hit.Modified) }
            : new[] { hit.Name, ParentOf(hit.Path), size, LocalFileSystem.FormatTime(hit.Modified), match };
        return new FileListRow(hit.Path, cells, hit.Name, false, false, hit.IsDirectory, hit);
    }

    /// <summary>The folder a hit lives in. For a member of an archive that is a path inside the archive.</summary>
    public static string ParentOf(string path)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        return parent ?? "";
    }
}
