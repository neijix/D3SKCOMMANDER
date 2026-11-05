namespace D3skCommander.Core.MultiRename;

public static partial class MultiRename
{
    // v0.1 compares paths case-insensitively where the usual volumes do.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string Key(string path)
    {
        if (PathComparison == StringComparison.OrdinalIgnoreCase)
        {
            return path.ToLowerInvariant();
        }

        return path;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static bool MetadataExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool SameFile(string left, string right)
    {
        try
        {
            if (!MetadataExists(left) || !MetadataExists(right))
            {
                return false;
            }

            return string.Equals(Resolve(left), Resolve(right), PathComparison);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        FileSystemInfo info = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
        var final = info.ResolveLinkTarget(returnFinalTarget: true);
        return final is null ? full : final.FullName;
    }
}
