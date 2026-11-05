namespace D3skCommander.Core.FileSystem;

/// <summary>
/// The volumes a user can open: drive letters on Windows, <c>/Volumes</c> and the root on macOS,
/// real mounts on Linux (no proc, sysfs, tmpfs, overlay or snap images).
/// </summary>
public static class DriveList
{
    private static readonly string[] HiddenFormats =
    [
        "proc", "sysfs", "tmpfs", "devtmpfs", "devpts", "overlay", "cgroup", "cgroup2", "securityfs",
        "debugfs", "tracefs", "configfs", "fusectl", "pstore", "bpf", "mqueue", "hugetlbfs", "autofs",
        "binfmt_misc", "ramfs", "efivarfs", "nsfs", "squashfs", "fuse.gvfsd-fuse", "fuse.portal",
    ];

    private static readonly string[] HiddenPrefixes = ["/proc", "/sys", "/dev", "/snap", "/run/snapd", "/var/lib/snapd", "/var/snap", "/boot/efi", "/System/Volumes", "/private/var/vm"];

    /// <summary>The volumes of this machine, best effort: an unreadable one is left out.</summary>
    public static IReadOnlyList<DriveEntry> Mounted()
    {
        var list = new List<DriveEntry>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return list;
        }

        foreach (var drive in drives)
        {
            var entry = Describe(drive);
            if (entry is not null && IsUseful(entry.Path, entry.Format))
            {
                list.Add(entry);
            }
        }

        return list;
    }

    /// <summary>False for a pseudo file system, a snap image or a system mount nobody browses.</summary>
    public static bool IsUseful(string mountPoint, string format)
    {
        ArgumentNullException.ThrowIfNull(mountPoint);
        ArgumentNullException.ThrowIfNull(format);
        if (format.Length > 0 && Array.Exists(HiddenFormats, hidden => string.Equals(hidden, format, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        foreach (var prefix in HiddenPrefixes)
        {
            if (mountPoint == prefix || mountPoint.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The text of a drive button or list line: <c>Label (path) - 12.0 GB free</c>.</summary>
    public static string Line(DriveEntry drive, string freeWord)
    {
        ArgumentNullException.ThrowIfNull(drive);
        ArgumentNullException.ThrowIfNull(freeWord);
        var name = drive.Label.Length == 0 || drive.Label == drive.Path ? drive.Path : drive.Label + " (" + drive.Path + ")";
        return drive.TotalBytes == 0
            ? name
            : name + " - " + LocalFileSystem.FormatSize(drive.FreeBytes) + " " + freeWord;
    }

    private static DriveEntry? Describe(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return null;
            }

            var path = drive.RootDirectory.FullName;
            var label = SafeLabel(drive, path);
            return new DriveEntry(
                path,
                label,
                drive.DriveFormat,
                (ulong)Math.Max(0, drive.AvailableFreeSpace),
                (ulong)Math.Max(0, drive.TotalSize));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string SafeLabel(DriveInfo drive, string path)
    {
        if (OperatingSystem.IsLinux())
        {
            var name = Path.GetFileName(path.TrimEnd('/'));
            return name.Length > 0 ? name : path;
        }

        var label = drive.VolumeLabel;
        return string.IsNullOrEmpty(label) ? path : label;
    }
}
