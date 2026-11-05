using D3skCommander.Core.Common;

namespace D3skCommander.Core.FileSystem;

/// <summary>What a file, folder or link is.</summary>
public enum EntryKind
{
    File,
    Folder,
    Link,
}

/// <summary>The facts the Properties dialog shows for one entry. A folder's size is measured apart.</summary>
public sealed partial record EntryProperties(
    string Name,
    string FullPath,
    EntryKind Kind,
    ulong Size,
    DateTimeOffset? Created,
    DateTimeOffset? Modified,
    DateTimeOffset? Accessed,
    string Attributes,
    string? Owner,
    string? LinkTarget)
{
    /// <summary>Reads the entry without following a link. A missing or unreadable path is a typed failure.</summary>
    public static FileResult<EntryProperties> Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (!info.Exists && info.LinkTarget is null)
            {
                return FileResult<EntryProperties>.Fail(OperationError.NotFound(path));
            }

            var target = info.LinkTarget;
            var kind = target is not null ? EntryKind.Link : (info is DirectoryInfo ? EntryKind.Folder : EntryKind.File);
            var size = info is FileInfo file && file.Exists ? (ulong)file.Length : 0UL;
            return FileResult<EntryProperties>.Ok(new EntryProperties(
                info.Name,
                info.FullName,
                kind,
                size,
                Stamp(info.CreationTimeUtc),
                Stamp(info.LastWriteTimeUtc),
                Stamp(info.LastAccessTimeUtc),
                AttributesOf(info),
                OwnerOf(info.FullName),
                target));
        }
        catch (UnauthorizedAccessException ex)
        {
            return FileResult<EntryProperties>.Fail(OperationError.AccessDenied(ex.Message, path));
        }
        catch (IOException ex)
        {
            return FileResult<EntryProperties>.Fail(OperationError.Other(ex.Message, path, ex.HResult));
        }
    }

    /// <summary>Files, folders and bytes of a selection; a folder counts its whole tree. Stops early when cancelled.</summary>
    public static SelectionSize Measure(IReadOnlyList<string> paths, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ulong bytes = 0;
        ulong files = 0;
        ulong folders = 0;
        foreach (var path in paths)
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            if (Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null)
            {
                folders++;
                var inside = LocalFileSystem.DirectorySize(path, cancel);
                bytes += inside.Bytes;
                files += inside.Files;
                folders += inside.Directories;
                continue;
            }

            files++;
            bytes += FileLength(path);
        }

        return new SelectionSize(bytes, files, folders);
    }

    private static ulong FileLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (ulong)info.Length : 0UL;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static DateTimeOffset? Stamp(DateTime utc)
    {
        // The OS answers 1601 or 1970 for "unknown".
        if (utc.Year < 1971)
        {
            return null;
        }

        return new DateTimeOffset(utc, TimeSpan.Zero).ToLocalTime();
    }

    private static string AttributesOf(FileSystemInfo info)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                return UnixFiles.Format(info.UnixFileMode);
            }
            catch (IOException)
            {
                return "";
            }
        }

        var parts = new List<string>();
        var flags = info.Attributes;
        if ((flags & FileAttributes.ReadOnly) != 0)
        {
            parts.Add("R");
        }

        if ((flags & FileAttributes.Hidden) != 0)
        {
            parts.Add("H");
        }

        if ((flags & FileAttributes.System) != 0)
        {
            parts.Add("S");
        }

        if ((flags & FileAttributes.Archive) != 0)
        {
            parts.Add("A");
        }

        return string.Concat(parts);
    }
}

/// <summary>Totals of a selection: bytes, files and folders (the chosen ones and those inside).</summary>
public readonly record struct SelectionSize(ulong Bytes, ulong Files, ulong Folders);
