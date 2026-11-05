using System.Text;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Catalog;

public static partial class DiskCatalog
{
    /// <summary>
    /// Read a catalog. An empty file and a truncated last line yield a document instead of an error.
    /// A truncated last line is dropped; a broken line before it is invalid data.
    /// </summary>
    public static FileResult<CatalogDocument> Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return Parse(reader, path);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return FileResult<CatalogDocument>.Fail(OperationError.FromException(ex, path));
        }
    }

    /// <summary>True when the extension is <c>.d3cat</c> and the first line is a schema-1 header. Plain text is not a catalog.</summary>
    public static bool IsCatalog(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!HasCatalogExtension(path))
        {
            return false;
        }

        var line = FirstLine(path);
        return line is not null && CatalogJson.TryHeader(line, out _);
    }

    private static FileResult<CatalogDocument> Parse(StreamReader reader, string path)
    {
        CatalogHeader? header = null;
        var entries = new List<CatalogEntry>();
        string? pending = null;
        var started = false;
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            if (pending is not null)
            {
                var failed = Accept(pending, path, ref header, entries, ref started);
                if (failed is not null)
                {
                    return FileResult<CatalogDocument>.Fail(failed);
                }
            }

            pending = line;
        }

        if (pending is not null && CatalogJson.IsObject(pending))
        {
            var failed = Accept(pending, path, ref header, entries, ref started);
            if (failed is not null)
            {
                return FileResult<CatalogDocument>.Fail(failed);
            }
        }

        if (!started)
        {
            return FileResult<CatalogDocument>.Ok(new CatalogDocument(null, []));
        }

        return FileResult<CatalogDocument>.Ok(new CatalogDocument(header, entries));
    }

    private static OperationError? Accept(
        string line,
        string path,
        ref CatalogHeader? header,
        List<CatalogEntry> entries,
        ref bool started)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        if (!started)
        {
            if (!CatalogJson.TryHeader(line, out var parsed) || parsed is null)
            {
                return OperationError.InvalidData("invalid catalog header", path);
            }

            header = parsed;
            started = true;
            return null;
        }

        if (!CatalogJson.TryEntry(line, out var entry) || entry is null)
        {
            return OperationError.InvalidData("invalid catalog entry", path);
        }

        entries.Add(entry);
        return null;
    }

    private static bool HasCatalogExtension(string path) =>
        Path.GetExtension(path).Equals(FileExtension, StringComparison.OrdinalIgnoreCase);

    private static string? FirstLine(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[8192];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return null;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, read).TrimStart('\uFEFF');
            var end = text.IndexOf('\n');
            var line = end < 0 ? text : text[..end];
            line = line.TrimEnd('\r');
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return null;
        }
    }
}
