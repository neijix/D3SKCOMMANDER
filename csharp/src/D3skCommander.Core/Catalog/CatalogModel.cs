using D3skCommander.Core.Common;

namespace D3skCommander.Core.Catalog;

/// <summary>One file or folder recorded in a disk catalog. <see cref="Path"/> is relative and uses <c>/</c>.</summary>
public sealed class CatalogEntry
{
    public CatalogEntry(string path, long size, DateTimeOffset? mtime, bool isDir)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        Path = path;
        Size = size;
        Mtime = mtime;
        IsDir = isDir;
    }

    public string Path { get; }

    public long Size { get; }

    /// <summary>Last write time in UTC, when the catalog recorded one.</summary>
    public DateTimeOffset? Mtime { get; }

    public bool IsDir { get; }
}

/// <summary>The first line of a <c>.d3cat</c> file: schema, root folder, and when the catalog was written.</summary>
public sealed class CatalogHeader
{
    public CatalogHeader(int schema, string root, DateTimeOffset created)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schema);
        ArgumentException.ThrowIfNullOrEmpty(root);
        Schema = schema;
        Root = root;
        Created = created;
    }

    public int Schema { get; }

    public string Root { get; }

    public DateTimeOffset Created { get; }
}

/// <summary>
/// A catalog read from disk. An empty or truncated file has a null <see cref="Header"/> and no entries.
/// </summary>
public sealed class CatalogDocument
{
    public CatalogDocument(CatalogHeader? header, IReadOnlyList<CatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Header = header;
        Entries = Copy(entries);
    }

    public CatalogHeader? Header { get; }

    public IReadOnlyList<CatalogEntry> Entries { get; }

    private static CatalogEntry[] Copy(IReadOnlyList<CatalogEntry> entries)
    {
        var copy = new CatalogEntry[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry is null)
            {
                throw new ArgumentException("An entry is missing.", nameof(entries));
            }

            copy[i] = entry;
        }

        return copy;
    }
}

/// <summary>How many entries were written, or the typed error. A failure leaves no output file.</summary>
public sealed class CatalogWriteResult
{
    private CatalogWriteResult(int count, OperationError? error)
    {
        Count = count;
        Error = error;
    }

    public int Count { get; }

    public OperationError? Error { get; }

    public bool IsSuccess => Error is null;

    public static CatalogWriteResult Ok(int count) => new(count, null);

    public static CatalogWriteResult Fail(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new CatalogWriteResult(0, error);
    }
}
