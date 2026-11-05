using System.Globalization;
using D3skCommander.Core.Catalog;
using D3skCommander.Core.Dbf;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Viewer;

/// <summary>DBF and disk-catalog tables for the lister. Readers live in their own modules.</summary>
public static class ViewerTables
{
    /// <summary>A <c>.d3cat</c> that <see cref="DiskCatalog"/> accepts, or null when it should be shown as bytes.</summary>
    public static ViewerTable? TryCatalog(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!DiskCatalog.IsCatalog(path) || LargerThan(path, ViewerLimits.MaxCatalogSize))
        {
            return null;
        }

        var read = DiskCatalog.Read(path);
        if (!read.IsSuccess)
        {
            return null;
        }

        var header = read.Value.Header;
        if (header is null)
        {
            return null;
        }

        return FromCatalog(read.Value, header);
    }

    public static ViewerTable FromDbf(DbfTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var columns = new List<string>(table.Fields.Count + 1) { "#" };
        var widths = new List<int>(table.Fields.Count + 1) { 6 };
        foreach (var field in table.Fields)
        {
            columns.Add(field.Name);
            widths.Add(ColumnWidth(field));
        }

        var rows = new List<IReadOnlyList<string>>(table.Records.Count);
        for (var i = 0; i < table.Records.Count; i++)
        {
            var deleted = i < table.Deleted.Count && table.Deleted[i];
            rows.Add(DbfRow(i, table.Records[i], deleted));
        }

        return new ViewerTable(columns, widths, rows, Note(table));
    }

    private static ViewerTable FromCatalog(CatalogDocument document, CatalogHeader header)
    {
        var rows = new List<IReadOnlyList<string>>(document.Entries.Count);
        foreach (var entry in document.Entries)
        {
            rows.Add(CatalogRow(entry));
        }

        return new ViewerTable(["Path", "Size", "Date"], [60, 22, 19], rows, header.Root);
    }

    private static string[] CatalogRow(CatalogEntry entry)
    {
        var name = entry.Path;
        if (entry.IsDir)
        {
            name += "/";
        }

        var size = entry.IsDir ? "<DIR>" : ShownSize(entry.Size);
        return [name, size, LocalFileSystem.FormatTime(entry.Mtime)];
    }

    private static string ShownSize(long size)
    {
        var bytes = (ulong)size;
        return LocalFileSystem.FormatGrouped(bytes)
            + " ("
            + LocalFileSystem.FormatSize(bytes)
            + ")";
    }

    private static string[] DbfRow(int index, IReadOnlyList<string> record, bool deleted)
    {
        var row = new string[record.Count + 1];
        var mark = deleted ? " *" : "";
        row[0] = (index + 1).ToString(CultureInfo.InvariantCulture) + mark;
        for (var i = 0; i < record.Count; i++)
        {
            row[i + 1] = record[i].TrimEnd();
        }

        return row;
    }

    private static string Note(DbfTable table)
    {
        var shown = table.Records.Count;
        var total = table.TotalRecords;
        var culture = CultureInfo.InvariantCulture;
        if ((uint)shown < total)
        {
            return shown.ToString(culture) + " / " + total.ToString(culture);
        }

        return total.ToString(culture);
    }

    private static int ColumnWidth(DbfField field)
    {
        var name = field.Name.Length;

        if (name > 40)
        {
            return 40;
        }

        return Math.Clamp((int)field.Length, name, 40);
    }

    private static bool LargerThan(string path, long limit)
    {
        try
        {
            return new FileInfo(path).Length > limit;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
