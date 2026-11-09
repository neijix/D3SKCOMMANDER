namespace D3skCommander.Core.DiskUsage;

/// <summary>
/// What a scan is expected to find, for a progress bar: the file count of the last scan of the same folder, else
/// the used space of the drive(s) when the folder is a drive root. Empty when nothing is known.
/// </summary>
public readonly record struct ScanEstimate(long Files, long Bytes)
{
    public static ScanEstimate None => default;

    public bool IsKnown => Files > 0 || Bytes > 0;

    /// <summary>How far <paramref name="seen"/> is, 0 to 99 while scanning (the estimate can be off); null when unknown.</summary>
    public double? Percent(ScanProgress seen)
    {
        double? fraction = Files > 0 ? (double)seen.Files / Files
            : Bytes > 0 ? (double)seen.Bytes / Bytes
            : null;
        return fraction is { } value ? Math.Clamp(Math.Floor(value * 100), 0, 99) : null;
    }

    /// <summary>
    /// The used space of the drives whose root is <paramref name="folder"/>, or of every one of <paramref name="drives"/>
    /// (an all-drives scan). <paramref name="used"/> reads one root's used bytes, null when it cannot.
    /// </summary>
    public static ScanEstimate FromDrives(string folder, IReadOnlyList<string>? drives, Func<string, long?> used)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(used);
        if (drives is not null)
        {
            var sum = drives.Sum(drive => used(drive) ?? 0);
            return new ScanEstimate(0, sum);
        }

        var root = Path.GetPathRoot(folder);
        var isRoot = !string.IsNullOrEmpty(root) && string.Equals(Trim(root), Trim(folder), StringComparison.OrdinalIgnoreCase);
        return isRoot && used(folder) is { } bytes ? new ScanEstimate(0, bytes) : None;
    }

    private static string Trim(string path) => path.Length > 1 ? path.TrimEnd('\\', '/') : path;
}
