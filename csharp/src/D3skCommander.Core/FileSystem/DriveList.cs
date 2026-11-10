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

    /// <summary>The drive whose mount point is the longest folder prefix of <paramref name="path"/>, or null.</summary>
    public static DriveEntry? Holding(IReadOnlyList<DriveEntry> drives, string path) =>
        Holding(drives, path, OperatingSystem.IsWindows());

    /// <summary>
    /// Same, with the path rules named: <paramref name="windows"/> ignores case and takes both
    /// separators; otherwise only <c>/</c> separates and case counts. Whole segments only.
    /// </summary>
    public static DriveEntry? Holding(IReadOnlyList<DriveEntry> drives, string path, bool windows)
    {
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(path);
        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var target = Normalize(path, windows);
        DriveEntry? best = null;
        var bestLength = -1;
        foreach (var drive in drives)
        {
            var mount = Normalize(drive.Path, windows);
            var inside = target.Equals(mount, comparison) || target.StartsWith(mount + (windows ? '\\' : '/'), comparison);
            if (inside && mount.Length > bestLength)
            {
                best = drive;
                bestLength = mount.Length;
            }
        }

        return best;
    }

    /// <summary>A short button name: <c>C:</c>, the mount folder's name, or <c>/</c> for the root.</summary>
    public static string ShortName(string mountPath)
    {
        ArgumentNullException.ThrowIfNull(mountPath);
        var trimmed = mountPath.TrimEnd('/', '\\');
        if (trimmed.Length == 0)
        {
            return mountPath.Length == 0 ? "" : "/";
        }

        var cut = trimmed.LastIndexOfAny(['/', '\\']);
        var name = cut < 0 ? trimmed : trimmed[(cut + 1)..];
        return Shorten(GvfsName(name) ?? name);
    }

    // A desktop network mount folder is named "smb-share:server=nas,share=downloads" or
    // "sftp:host=box,user=me": the share (else the host or server) is the useful part.
    private static string? GvfsName(string name)
    {
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || !name.Contains('=', StringComparison.Ordinal))
        {
            return null;
        }

        var pairs = name[(colon + 1)..].Split(',')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[1].Length > 0)
            .ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        foreach (var key in (string[])["share", "host", "server"])
        {
            if (pairs.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string Shorten(string name) => name.Length <= 16 ? name : name[..15] + "…";

    private static string Normalize(string path, bool windows)
    {
        var text = windows ? path.Replace('/', '\\') : path;
        return text.TrimEnd(windows ? '\\' : '/');
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
                (ulong)Math.Max(0, drive.TotalSize),
                drive.DriveType == DriveType.Network);
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
