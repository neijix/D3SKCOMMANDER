using System.IO.Enumeration;
using D3skCommander.Core.Common;

namespace D3skCommander.Core.FileSystem;

public static partial class LocalFileSystem
{
    // The children with the status the enumeration already read: one system call per entry
    // instead of the ten of a per-path read, which is what keeps a 100k-entry folder quick.
    private static FileResult<List<(string Path, FileSystemInfo Info)>> ReadChildStatus(string directory)
    {
        try
        {
            var enumeration = new FileSystemEnumerable<(string, FileSystemInfo)>(
                directory,
                static (ref FileSystemEntry entry) => (entry.ToSpecifiedFullPath(), entry.ToFileSystemInfo()),
                AllEntries());
            return FileResult<List<(string, FileSystemInfo)>>.Ok([.. enumeration]);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return FileResult<List<(string, FileSystemInfo)>>.Fail(OperationError.FromException(ex, directory));
        }
    }

    // A link needs its followed target (folder or not, its mode): that one takes the per-path read.
    private static FileResult<Entry> ReadListed(string path, FileSystemInfo info)
    {
        try
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return ReadEntry(path);
            }

            return FileResult<Entry>.Ok(FromStatus(path, info));
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return ReadEntry(path);
        }
    }

    // The same fields as BuildEntry, read from the cached status of a file that is not a link.
    private static Entry FromStatus(string path, FileSystemInfo info)
    {
        var name = FileNameOf(path);
        var attributes = info.Attributes;
        var directory = info is DirectoryInfo;
        var length = directory || info is not FileInfo file ? 0 : Math.Max(0, file.Length);
        var utc = info.LastWriteTimeUtc;
        bool readOnly;
        string text;
        if (OperatingSystem.IsWindows())
        {
            readOnly = (attributes & FileAttributes.ReadOnly) != 0;
            text = WindowsAttributes(attributes);
        }
        else
        {
            var mode = UnixFiles.ModeOf(info);
            readOnly = (mode & UnixFileMode.UserWrite) == 0;
            text = UnixFiles.Format(mode);
        }

        var hidden = (name.StartsWith('.') && name != "..")
            || (OperatingSystem.IsWindows() && (attributes & FileAttributes.Hidden) != 0);
        return new Entry(
            name,
            path,
            directory,
            false,
            false,
            (ulong)length,
            new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            hidden,
            readOnly,
            text);
    }
}
