using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Ftp;

namespace D3skCommander.App.Features.Ftp;

/// <summary>The rows of the shared file list for a remote folder (entries map through <see cref="FtpFileSystemProvider.ToEntry"/>).</summary>
internal static class FtpRows
{
    /// <summary>The ".." row that leads to the parent of <paramref name="directory"/>; null at the top.</summary>
    public static Entry? ParentEntry(string directory)
    {
        var parent = FtpPaths.Parent(directory);
        if (parent.Length == 0 || parent == directory)
        {
            return null;
        }

        return new Entry("..", parent, true, false, true, 0, null, false, false, "");
    }

    /// <summary>The rows shown for a folder: the parent row first, then its entries.</summary>
    public static List<Entry> Rows(string directory, IReadOnlyList<Entry> entries)
    {
        var rows = new List<Entry>(entries.Count + 1);
        var parent = ParentEntry(directory);
        if (parent is not null)
        {
            rows.Add(parent);
        }

        rows.AddRange(entries);
        return rows;
    }
}
