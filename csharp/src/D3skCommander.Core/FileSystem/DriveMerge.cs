using D3skCommander.Core.NetDrives;

namespace D3skCommander.Core.FileSystem;

/// <summary>
/// The volumes and the connected network shares as one list, each drive once: a share mapped to a drive
/// letter is the same drive as that volume (<c>N:</c> and <c>N:\</c> match, case aside on Windows).
/// </summary>
public static class DriveMerge
{
    private static readonly string[] NetworkFormats = ["cifs", "smb3", "smbfs", "nfs", "nfs4", "afpfs", "webdav", "davfs", "fuse.sshfs"];

    /// <summary>
    /// The <paramref name="volumes"/> in order, <see cref="DriveEntry.Network"/> set on the ones that are network
    /// drives (a matching share also lends its label), then the shares no volume stands for.
    /// </summary>
    public static IReadOnlyList<DriveEntry> Merge(IReadOnlyList<DriveEntry> volumes, IReadOnlyList<ConnectedShare> shares, bool windows)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(shares);
        var comparer = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var list = new List<DriveEntry>(volumes.Count + shares.Count);
        foreach (var volume in volumes)
        {
            var share = shares.FirstOrDefault(s => comparer.Equals(Normalize(s.Path, windows), Normalize(volume.Path, windows)));
            if (share is null)
            {
                list.Add(volume.Network || IsNetworkFormat(volume.Format) ? volume with { Network = true } : volume);
            }
            else
            {
                list.Add(volume with { Network = true, Label = share.Label.Length > 0 ? share.Label : volume.Label });
            }
        }

        foreach (var share in shares)
        {
            if (!volumes.Any(volume => comparer.Equals(Normalize(volume.Path, windows), Normalize(share.Path, windows))))
            {
                list.Add(new DriveEntry(RootOf(share.Path, windows), share.Label, "", 0, 0, Network: true));
            }
        }

        return list;
    }

    /// <summary>True for a file system that lives on another machine (cifs, nfs, sshfs…).</summary>
    public static bool IsNetworkFormat(string format) =>
        NetworkFormats.Contains(format, StringComparer.OrdinalIgnoreCase);

    // A bare "Z:" counts as "Z:\"; a trailing separator never matters.
    private static string Normalize(string path, bool windows)
    {
        var text = windows ? path.Replace('/', '\\') : path;
        return text.TrimEnd(windows ? '\\' : '/');
    }

    private static string RootOf(string path, bool windows) =>
        windows && path.Length == 2 && path[1] == ':' ? path + "\\" : path;
}
