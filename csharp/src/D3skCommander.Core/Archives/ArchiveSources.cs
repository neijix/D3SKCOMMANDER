using D3skCommander.Core.Common;

namespace D3skCommander.Core.Archives;

internal sealed class SourceItem
{
    public SourceItem(string diskPath, string name, bool isDirectory, long size, DateTime? modified)
    {
        DiskPath = diskPath;
        Name = name;
        IsDirectory = isDirectory;
        Size = size;
        Modified = modified;
    }

    public string DiskPath { get; }

    public string Name { get; }

    public bool IsDirectory { get; }

    public long Size { get; }

    public DateTime? Modified { get; }
}

/// <summary>
/// Each source is stored under its own file name, recursively. A symlink to a file is stored
/// as a file; other symlinks are skipped. The archive being written is never stored in itself.
/// </summary>
internal static class ArchiveSources
{
    public static ArchiveResult<List<SourceItem>> Collect(IReadOnlyList<string> sources, string archive, string part)
    {
        var items = new List<SourceItem>();
        foreach (var source in sources)
        {
            if (string.IsNullOrEmpty(source))
            {
                return ArchiveResult<List<SourceItem>>.Fail(
                    OperationError.InvalidData("cannot archive "));
            }

            var baseName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(baseName))
            {
                return ArchiveResult<List<SourceItem>>.Fail(
                    OperationError.InvalidData($"cannot archive {source}"));
            }

            try
            {
                Walk(source, source, baseName, archive, part, items);
            }
            catch (Exception ex) when (ArchiveErrors.IsIo(ex))
            {
                return ArchiveResult<List<SourceItem>>.Fail(ArchiveErrors.From(ex, source));
            }
        }

        return ArchiveResult<List<SourceItem>>.Ok(items);
    }

    private static void Walk(
        string path,
        string root,
        string baseName,
        string archive,
        string part,
        List<SourceItem> items)
    {
        if (Same(path, archive) || Same(path, part))
        {
            return;
        }

        if (IsReparse(path))
        {
            if (IsFollowedFile(path))
            {
                items.Add(Item(path, root, baseName, false));
            }

            return;
        }

        if (Directory.Exists(path))
        {
            items.Add(Item(path, root, baseName, true));
            var children = Directory.GetFileSystemEntries(path);
            Array.Sort(children, StringComparer.Ordinal);
            foreach (var child in children)
            {
                Walk(child, root, baseName, archive, part, items);
            }

            return;
        }

        if (File.Exists(path))
        {
            items.Add(Item(path, root, baseName, false));
        }
        else
        {
            throw new FileNotFoundException("not found", path);
        }
    }

    private static SourceItem Item(string path, string root, string baseName, bool isDirectory)
    {
        var name = baseName;
        var relative = Path.GetRelativePath(root, path);
        if (relative != ".")
        {
            name = baseName + "/" + relative.Replace('\\', '/');
        }

        var size = isDirectory ? 0 : LengthOf(path);
        return new SourceItem(path, name, isDirectory, size, Modified(path));
    }

    private static bool IsReparse(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ArchiveErrors.IsIo(ex))
        {
            return false;
        }
    }

    private static bool IsFollowedFile(string path) => File.Exists(path) && !Directory.Exists(path);

    private static long LengthOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ArchiveErrors.IsIo(ex))
        {
            return 0;
        }
    }

    private static DateTime? Modified(string path)
    {
        try
        {
            return File.GetLastWriteTime(path);
        }
        catch (Exception ex) when (ArchiveErrors.IsIo(ex))
        {
            return null;
        }
    }

    private static bool Same(string left, string right)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch (Exception ex) when (ArchiveErrors.IsIo(ex))
        {
            return false;
        }
    }
}
