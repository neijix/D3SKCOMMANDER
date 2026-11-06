using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Panels;

/// <summary>
/// Counts and byte totals shown in a panel's status line. The ".." row is never counted. A folder
/// counts its bytes once they are measured (an unmeasured folder carries size 0).
/// </summary>
/// <param name="Selected">Marked entries.</param>
/// <param name="Count">Entries other than "..".</param>
/// <param name="SelectedBytes">Bytes of the marked entries.</param>
/// <param name="TotalBytes">Bytes of all entries other than "..".</param>
public readonly record struct PanelTotals(int Selected, int Count, ulong SelectedBytes, ulong TotalBytes)
{
    /// <summary>Totals of <paramref name="entries"/> with the paths in <paramref name="marked"/> marked.</summary>
    public static PanelTotals Compute(IReadOnlyList<Entry> entries, IReadOnlyCollection<string> marked)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(marked);
        var set = marked.Count == 0 ? null : new HashSet<string>(marked, StringComparer.Ordinal);
        var selected = 0;
        var count = 0;
        ulong selectedBytes = 0;
        ulong totalBytes = 0;
        foreach (var entry in entries)
        {
            if (entry.IsParent)
            {
                continue;
            }

            count++;
            totalBytes += entry.Size;
            if (set is not null && set.Contains(entry.Path))
            {
                selected++;
                selectedBytes += entry.Size;
            }
        }

        return new PanelTotals(selected, count, selectedBytes, totalBytes);
    }

    /// <summary>Bytes of all entries other than "..".</summary>
    public static ulong SumBytes(IReadOnlyList<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ulong total = 0;
        foreach (var entry in entries)
        {
            if (!entry.IsParent)
            {
                total += entry.Size;
            }
        }

        return total;
    }

    /// <summary>Bytes of the entries whose path is in <paramref name="marked"/>.</summary>
    public static ulong MarkedBytes(IReadOnlyList<Entry> entries, IReadOnlyCollection<string> marked) =>
        Compute(entries, marked).SelectedBytes;

    /// <summary>
    /// Free bytes of the volume holding the local folder <paramref name="path"/>, or null when it is
    /// not a folder on a local volume (archive, FTP, virtual list) or the volume cannot be read. Does I/O.
    /// </summary>
    public static ulong? FreeSpace(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return null;
        }

        try
        {
            var root = OperatingSystem.IsWindows() ? Path.GetPathRoot(path) : path;
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return (ulong)Math.Max(0, new DriveInfo(root).AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
