using D3skCommander.Core.FileKinds;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;

namespace D3skCommander.Core.Panels;

/// <summary>
/// The text of the columns an <see cref="Entry"/> does not carry: created, accessed, owner, group and kind.
/// Each one asks the disk, so a view calls it only for a column the active set shows and for rows it draws.
/// </summary>
public static class ColumnValues
{
    public static string Created(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Time(entry, File.GetCreationTimeUtc);
    }

    public static string Accessed(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Time(entry, File.GetLastAccessTimeUtc);
    }

    public static string Owner(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsParent ? "" : UnixOwners.OwnerOf(entry.Path) ?? "";
    }

    public static string Group(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsParent ? "" : UnixOwners.GroupOf(entry.Path) ?? "";
    }

    /// <summary>A short type text: "Folder", "Link", "PNG image", "Text", "ZIP archive", "File". Decided from the name only.</summary>
    public static string Kind(Entry entry, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(text);
        var kind = FileKindClassifier.Of(entry);
        if (kind == FileKind.Parent)
        {
            return "";
        }

        var extension = entry.Extension.TrimStart('.').ToUpperInvariant();
        var key = "kind." + kind.ToString().ToLowerInvariant();
        if (extension.Length == 0 && kind is not (FileKind.Folder or FileKind.Link or FileKind.Text))
        {
            return text.Get("kind.file");
        }

        return text.Get(key).Replace("{ext}", extension, StringComparison.Ordinal);
    }

    private static string Time(Entry entry, Func<string, DateTime> read)
    {
        if (entry.IsParent)
        {
            return "";
        }

        try
        {
            // A missing path reads as year 1601 (or 1970 on Unix): nothing real is older than 1980 here.
            var utc = read(entry.Path);
            if (utc.Year < 1980)
            {
                return "";
            }

            return LocalFileSystem.FormatTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "";
        }
    }
}
