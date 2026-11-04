using D3skCommander.Core.Common;

namespace D3skCommander.Core.FileSystem;

public static partial class LocalFileSystem
{
    /// <summary>
    /// Lists a folder: a ".." row first when the folder has a parent, then every entry.
    /// Hidden ones only when <paramref name="showHidden"/>. Unreadable entries are skipped.
    /// </summary>
    public static FileResult<List<Entry>> ListDirectory(string directory, bool showHidden)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var children = ReadChildren(directory);
        if (!children.IsSuccess)
        {
            return FileResult<List<Entry>>.Fail(children.Error ?? OperationError.NotFound(directory));
        }

        var entries = new List<Entry>();
        AddParent(directory, entries);
        foreach (var child in children.Value)
        {
            var read = ReadEntry(child);
            if (!read.IsSuccess)
            {
                continue;
            }

            if (showHidden || !read.Value.Hidden)
            {
                entries.Add(read.Value);
            }
        }

        return FileResult<List<Entry>>.Ok(entries);
    }

    /// <summary>One path. A link is reported as a link; a link to a folder still counts as a folder.</summary>
    public static FileResult<Entry> ReadEntry(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            if (!Exists(path) && !IsLink(path))
            {
                return FileResult<Entry>.Fail(OperationError.NotFound(path));
            }

            return FileResult<Entry>.Ok(BuildEntry(path));
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return FileResult<Entry>.Fail(OperationError.FromException(ex, path));
        }
    }

    private static void AddParent(string directory, List<Entry> entries)
    {
        var parent = ParentPath(directory);
        if (parent is null)
        {
            return;
        }

        entries.Add(new Entry("..", parent, true, false, true, 0, null, false, false, ""));
    }

    private static string? ParentPath(string directory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(directory);
        if (trimmed.Length == 0)
        {
            return null;
        }

        return Path.GetDirectoryName(trimmed);
    }

    private static Entry BuildEntry(string path)
    {
        var name = FileNameOf(path);
        var readOnly = ReadOnlyOf(path);
        var directory = Directory.Exists(path);
        return new Entry(
            name,
            path,
            directory,
            IsLink(path),
            false,
            directory ? 0 : LengthOf(path),
            ModifiedOf(path),
            IsHidden(name, path),
            readOnly,
            AttributesOf(path, readOnly));
    }

    private static string FileNameOf(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return name.Length == 0 ? path : name;
    }

    private static ulong LengthOf(string path)
    {
        try
        {
            var length = new FileInfo(path).Length;
            return length <= 0 ? 0 : (ulong)length;
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return 0;
        }
    }

    private static DateTimeOffset? ModifiedOf(string path)
    {
        try
        {
            var utc = File.GetLastWriteTimeUtc(path);
            return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return null;
        }
    }

    // Dot-files everywhere; on Windows also the hidden attribute.
    private static bool IsHidden(string name, string path)
    {
        if (name.StartsWith('.') && name != "..")
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
    }

    private static bool ReadOnlyOf(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
            }

            return UnixFiles.IsReadOnly(path);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
    }

    private static string AttributesOf(string path, bool readOnly)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsAttributes(File.GetAttributes(path));
            }

            return UnixFiles.Attributes(path);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return readOnly ? "r" : "-";
        }
    }

    private static string WindowsAttributes(FileAttributes attributes)
    {
        var chars = new char[4];
        chars[0] = Flag(attributes, FileAttributes.ReadOnly, 'r');
        chars[1] = Flag(attributes, FileAttributes.Archive, 'a');
        chars[2] = Flag(attributes, FileAttributes.Hidden, 'h');
        chars[3] = Flag(attributes, FileAttributes.System, 's');
        return new string(chars);
    }

    private static char Flag(FileAttributes attributes, FileAttributes bit, char letter) =>
        (attributes & bit) != 0 ? letter : '-';
}
