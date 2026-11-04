using D3skCommander.Core.Common;

namespace D3skCommander.Core.FileOps;

/// <summary>v0.1 target rule, single-component rename, and <c>name (2).ext</c>.</summary>
public static class FileOpNames
{
    /// <summary>
    /// An existing folder, a trailing separator, or more than one source is the destination
    /// folder. Otherwise the parent is the folder and the last component is the new name.
    /// </summary>
    public static TransferTarget FromDialog(string target, int sourceCount, bool targetIsDirectory)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceCount);
        if (targetIsDirectory || sourceCount > 1 || EndsWithSeparator(target))
        {
            return new TransferTarget(Path.TrimEndingDirectorySeparator(target), null);
        }

        var directory = Parent(target);
        var name = Path.GetFileName(target);
        return new TransferTarget(directory ?? "", name.Length == 0 ? null : name);
    }

    /// <summary>
    /// Parent directory, with the separators the caller typed.
    /// <see cref="Path.GetDirectoryName"/> rewrites <c>/tmp/out</c> to <c>\tmp\out</c> on Windows.
    /// </summary>
    private static string? Parent(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !OperatingSystem.IsWindows() || directory.Length > path.Length)
        {
            return directory;
        }

        var typed = path[..directory.Length];
        return SamePathText(typed, directory) ? typed : directory;
    }

    private static bool SamePathText(string typed, string normalized)
    {
        if (typed.Length != normalized.Length)
        {
            return false;
        }

        for (var i = 0; i < typed.Length; i++)
        {
            if (typed[i] == normalized[i])
            {
                continue;
            }

            if (IsSeparator(typed[i]) && IsSeparator(normalized[i]))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsSeparator(char c) => c is '/' or '\\';

    /// <summary>
    /// <c>a.txt</c> stays free, otherwise <c>a (2).txt</c>, <c>a (3).txt</c>…
    /// A leading dot is not an extension.
    /// </summary>
    public static string UniqueName(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(name);
        var first = Path.Combine(directory, name);
        if (!Taken(first))
        {
            return first;
        }

        Split(name, out var stem, out var extension);
        for (var number = 2; number < 10000; number++)
        {
            var candidate = Path.Combine(directory, stem + " (" + number.ToString() + ")" + extension);
            if (!Taken(candidate))
            {
                return candidate;
            }
        }

        return first;
    }

    /// <summary>
    /// The rename of a single source, or null when the source keeps its name.
    /// A separator or an empty component is refused.
    /// </summary>
    public static OperationError? ValidateRename(string? renameTo, int sourceCount, out string? name)
    {
        name = null;
        if (string.IsNullOrEmpty(renameTo) || sourceCount != 1)
        {
            return null;
        }

        if (!IsSingleComponent(renameTo))
        {
            return OperationError.InvalidData("invalid file name: " + renameTo);
        }

        name = renameTo;
        return null;
    }

    internal static string? Leaf(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var name = Path.GetFileName(trimmed);
        return name.Length == 0 ? null : name;
    }

    internal static string Join(string directory, string name)
    {
        if (directory.Length == 0)
        {
            return name;
        }

        return Path.Combine(directory, name);
    }

    /// <summary>
    /// How two full paths compare: Windows and macOS volumes ignore case by default, so
    /// <c>Dir/a.txt</c> and <c>dir/a.txt</c> are the same file there and an overwrite would truncate the source.
    /// </summary>
    internal static StringComparison PathComparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static string Full(string path)
    {
        var full = Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(full);
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="ancestor"/> or a child of it.</summary>
    internal static bool IsInside(string ancestor, string path)
    {
        var relative = Path.GetRelativePath(ancestor, path);
        if (relative == ".")
        {
            return true;
        }

        if (Path.IsPathRooted(relative) || relative == "..")
        {
            return false;
        }

        if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        return !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool EndsWithSeparator(string path) =>
        path.EndsWith('/') || path.EndsWith('\\');

    private static bool IsSingleComponent(string name)
    {
        if (name == "." || name == "..")
        {
            return false;
        }

        if (name.Contains('/') || name.Contains('\\') || Path.IsPathRooted(name))
        {
            return false;
        }

        return true;
    }

    private static void Split(string name, out string stem, out string extension)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0)
        {
            stem = name;
            extension = "";
            return;
        }

        stem = name[..dot];
        extension = name[dot..];
    }

    private static bool Taken(string path)
    {
        try
        {
            if (new FileInfo(path).LinkTarget is not null)
            {
                return true;
            }

            File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }
}
