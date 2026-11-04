namespace D3skCommander.Core.FileSystem;

public static partial class LocalFileSystem
{
    /// <summary>
    /// Branch view: every file below <paramref name="directory"/>, named by its relative path.
    /// Stops after <paramref name="limit"/> files or when <paramref name="cancel"/> is requested.
    /// Hidden directories are not descended into.
    /// </summary>
    public static List<Entry> ListBranch(string directory, bool showHidden, int limit, CancellationToken cancel)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        var found = new List<Entry>();
        Walk(directory, directory, showHidden, limit, cancel, found);
        return found;
    }

    private static bool Walk(
        string root,
        string directory,
        bool showHidden,
        int limit,
        CancellationToken cancel,
        List<Entry> found)
    {
        var read = ReadChildren(directory);
        if (!read.IsSuccess)
        {
            return true;
        }

        foreach (var child in read.Value)
        {
            if (Stopped(cancel, found.Count, limit))
            {
                return false;
            }

            var name = Path.GetFileName(child);
            if (!showHidden && IsHidden(name, child))
            {
                continue;
            }

            if (IsRealDirectory(child))
            {
                if (!Walk(root, child, showHidden, limit, cancel, found))
                {
                    return false;
                }

                continue;
            }

            AddBranchFile(root, child, found);
        }

        return true;
    }

    private static bool Stopped(CancellationToken cancel, int count, int limit) =>
        cancel.IsCancellationRequested || count >= limit;

    private static void AddBranchFile(string root, string child, List<Entry> found)
    {
        var read = ReadEntry(child);
        if (!read.IsSuccess)
        {
            return;
        }

        var entry = read.Value;
        var relative = RelativeName(root, child);
        if (relative is not null)
        {
            entry.Name = relative;
        }

        found.Add(entry);
    }

    private static string? RelativeName(string root, string child)
    {
        var relative = Path.GetRelativePath(root, child);
        if (relative.Length == 0 || Path.IsPathRooted(relative) || relative == "..")
        {
            return null;
        }

        if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return relative;
    }
}
