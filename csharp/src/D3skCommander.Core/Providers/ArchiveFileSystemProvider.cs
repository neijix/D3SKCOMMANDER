using D3skCommander.Core.Archives;

namespace D3skCommander.Core.Providers;

/// <summary>
/// Browses a ZIP, TAR, TAR.GZ or 7Z as a folder. The provider is read-only: create, write,
/// rename and delete are refused by <see cref="FileSystemProvider"/>. Copying out is an extract,
/// moving out is refused, and copying in tells the user to pack (<see cref="ArchiveTransfers"/>).
/// </summary>
public sealed partial class ArchiveFileSystemProvider : FileSystemProvider
{
    public override bool IsReadOnly => true;

    /// <summary>A native archive file on disk. External formats are not opened here.</summary>
    public static bool IsArchiveFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }

        return IsNative(ArchiveKinds.FromPath(path));
    }

    /// <summary>The path is an archive file, or a member path under one.</summary>
    public static bool IsArchivePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return Locate(path).Error is null;
    }

    /// <summary>Splits <paramref name="path"/> into the archive file and the member path inside it.</summary>
    public static ArchivePlace Locate(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var current = Path.TrimEndingDirectorySeparator(path);
        var parts = new List<string>();
        while (current.Length > 0)
        {
            if (IsArchiveFile(current))
            {
                parts.Reverse();
                return ArchivePlace.Found(current, string.Join('/', parts));
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current)
            {
                break;
            }

            parts.Add(Path.GetFileName(current));
            current = parent;
        }

        return ArchivePlace.Missing(path);
    }

    internal static string MemberPath(string archive, string inner)
    {
        var path = archive;
        if (inner.Length == 0)
        {
            return path;
        }

        foreach (var part in inner.Split('/'))
        {
            if (part.Length > 0)
            {
                path = Path.Combine(path, part);
            }
        }

        return path;
    }

    private static bool IsNative(ArchiveKind? kind) =>
        kind == ArchiveKind.Zip
        || kind == ArchiveKind.Tar
        || kind == ArchiveKind.TarGz
        || kind == ArchiveKind.SevenZ;
}
