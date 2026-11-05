using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.Catalog;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Catalog;

/// <summary>Rows for one offline catalog search. The cells come from the catalog file, not from the disk tree.</summary>
internal static class CatalogHits
{
    public static IReadOnlyList<FileListColumn> Columns { get; } =
    [
        new FileListColumn("name", 1, true),
        new FileListColumn("path", 240),
        new FileListColumn("size", FileListEntryRows.SizeWidth),
        new FileListColumn("date", FileListEntryRows.DateWidth),
    ];

    public static CatalogHitList Load(string path, string query)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(query);
        var read = DiskCatalog.Read(path);
        if (!read.IsSuccess)
        {
            return CatalogHitList.Failed(read.Error ?? NotCatalog(path));
        }

        if (!DiskCatalog.IsCatalog(path) || read.Value.Header is null)
        {
            return CatalogHitList.Failed(NotCatalog(path));
        }

        return CatalogHitList.Found(read.Value, DiskCatalog.Search(read.Value, query));
    }

    public static FileListRow Row(CatalogEntry entry, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(text);
        var name = Name(entry.Path);
        var size = entry.IsDir ? text.Get("folder") : LocalFileSystem.FormatSize((ulong)entry.Size);
        var cells = new[] { name, entry.Path, size, LocalFileSystem.FormatTime(entry.Mtime) };
        return new FileListRow(entry.Path, cells, name, false, false, entry.IsDir, entry);
    }

    public static string Absolute(CatalogHeader header, CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(entry);
        var relative = entry.Path.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(header.Root, relative));
    }

    private static string Name(string path)
    {
        var slash = path.LastIndexOf('/');
        if (slash < 0)
        {
            return path;
        }

        return path[(slash + 1)..];
    }

    private static OperationError NotCatalog(string path) =>
        OperationError.InvalidData("not a catalog", path);
}

internal readonly struct CatalogHitList
{
    private CatalogHitList(CatalogDocument? document, IReadOnlyList<CatalogEntry>? hits, OperationError? error)
    {
        Document = document;
        Hits = hits;
        Error = error;
    }

    public CatalogDocument? Document { get; }

    public IReadOnlyList<CatalogEntry>? Hits { get; }

    public OperationError? Error { get; }

    public static CatalogHitList Found(CatalogDocument document, IReadOnlyList<CatalogEntry> hits)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(hits);
        return new CatalogHitList(document, hits, null);
    }

    public static CatalogHitList Failed(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new CatalogHitList(null, null, error);
    }
}
