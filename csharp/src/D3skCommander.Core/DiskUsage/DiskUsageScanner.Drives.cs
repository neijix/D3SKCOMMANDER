namespace D3skCommander.Core.DiskUsage;

public static partial class DiskUsageScanner
{
    /// <summary>
    /// One tree over several drives: a root with one child per drive, each scanned like <see cref="Scan"/> and named
    /// the same way. A folder in <paramref name="skip"/> (the OS mount points) other than the drive being scanned is
    /// neither walked nor counted. A drive that cannot be opened is an unreadable child of 0 bytes; the others are
    /// still scanned. Cancelling throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static SizeNode ScanDrives(
        string rootName,
        string rootPath,
        IEnumerable<string> drives,
        IEnumerable<string> skip,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancel = default,
        int topFiles = TopFiles)
    {
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(skip);
        var skipped = new HashSet<string>(
            skip.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Normal), PathComparer);
        var walk = new Walk(progress, cancel, Math.Max(0, topFiles), skipped);
        var children = new List<SizeNode>();
        foreach (var drive in drives)
        {
            cancel.ThrowIfCancellationRequested();
            string full;
            try
            {
                full = System.IO.Path.GetFullPath(drive);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                children.Add(SizeNode.Folder(drive, drive, [], unreadable: true));
                continue;
            }

            children.Add(walk.Drive(full, NameOf(full)));
        }

        walk.Report();
        return SizeNode.Folder(rootName, rootPath, children);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string Normal(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var trimmed = System.IO.Path.TrimEndingDirectorySeparator(full);
        return trimmed.Length == 0 ? full : trimmed;
    }

    private sealed partial class Walk
    {
        public SizeNode Drive(string full, string name)
        {
            if (!Directory.Exists(full))
            {
                return SizeNode.Folder(name, full, [], unreadable: true);
            }

            _folders++;
            return Folder(full, name);
        }
    }
}
