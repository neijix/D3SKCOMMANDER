using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

public sealed partial class ListFileSystemProvider
{
    /// <summary>
    /// Search results as one virtual folder named <paramref name="title"/> inside <paramref name="startFolder"/>,
    /// so ".." leads back to the folder the search started in. Rows are named by their path relative to
    /// <paramref name="startFolder"/> (the full path outside it), which keeps two same-named hits apart.
    /// A path that cannot be read, such as a member of an archive, is left out. Every listing checks the paths again.
    /// </summary>
    public static ListFileSystemProvider Results(string startFolder, string title, IEnumerable<string> paths)
    {
        ArgumentException.ThrowIfNullOrEmpty(startFolder);
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(paths);
        var entries = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!seen.Add(path))
            {
                continue;
            }

            var read = LocalFileSystem.ReadEntry(path);
            if (read.IsSuccess)
            {
                read.Value.Name = ResultName(startFolder, path);
                entries.Add(read.Value);
            }
        }

        var directory = Path.Combine(startFolder, SafeFolderName(title));
        return new ListFileSystemProvider(directory, entries, recheck: true);
    }

    private static string ResultName(string startFolder, string path)
    {
        var relative = Path.GetRelativePath(startFolder, path);
        var outside = relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return outside || Path.IsPathRooted(relative) ? path : relative;
    }

    private static string SafeFolderName(string title)
    {
        var bad = Path.GetInvalidFileNameChars();
        var chars = title.Select(c => Array.IndexOf(bad, c) >= 0 ? '_' : c).ToArray();
        return new string(chars);
    }
}
