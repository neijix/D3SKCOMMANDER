namespace D3skCommander.Core.NetDrives;

public static partial class NetDrive
{
    /// <summary>How many addresses the connect form remembers. Passwords are not addresses.</summary>
    public const int RecentLimit = 10;

    /// <summary>
    /// Remembers <paramref name="address"/> at the front, dropping a duplicate and keeping
    /// <see cref="RecentLimit"/> entries. Only the address is stored.
    /// </summary>
    public static IReadOnlyList<string> Remember(IReadOnlyList<string> recent, string address)
    {
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(address);
        var trimmed = address.Trim();
        var next = new List<string>(Math.Min(recent.Count + 1, RecentLimit)) { trimmed };
        foreach (var item in recent)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (next.Count == RecentLimit)
            {
                break;
            }

            if (!string.Equals(item, trimmed, StringComparison.Ordinal))
            {
                next.Add(item);
            }
        }

        return next;
    }

    /// <summary>
    /// A Windows connection (<c>WNetEnumResource</c>: remote and local name) as a share. A server login made through
    /// <c>\\server\IPC$</c> without a drive letter is the server root, <c>\\server\</c>, so Home lists the server.
    /// </summary>
    public static ConnectedShare ConnectedFromWnet(string remote, string? local)
    {
        ArgumentNullException.ThrowIfNull(remote);
        var server = UncPath.ShareServer(remote);
        var share = remote.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (local is null && server is not null && string.Equals(share, WindowsNet.IpcShare, StringComparison.OrdinalIgnoreCase))
        {
            return new ConnectedShare("\\\\" + server, UncPath.ServerRoot(server));
        }

        return new ConnectedShare(remote, local ?? remote);
    }

    /// <summary>
    /// Whether a share <c>NetShareEnum</c> reports is listed at <c>\\server\</c>: a disk share (type 0) that is
    /// neither special (bit <c>0x80000000</c>) nor hidden (a name ending in <c>$</c>).
    /// </summary>
    public static bool IsBrowsableShare(uint type, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return (type & 0xFF) == 0 && (type & 0x80000000) == 0 && name.Length > 0 && !name.EndsWith('$');
    }

    /// <summary>Shares GVFS has mounted under <paramref name="gvfsDirectory"/>, labelled by address.</summary>
    public static IReadOnlyList<ConnectedShare> ConnectedFromGvfs(string gvfsDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(gvfsDirectory);
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(gvfsDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return [];
        }

        var shares = new List<ConnectedShare>();
        foreach (var child in children)
        {
            var uri = GvfsUri(child);
            if (uri is null)
            {
                continue;
            }

            shares.Add(new ConnectedShare(uri.TrimEnd('/'), child));
        }

        shares.Sort((left, right) => string.CompareOrdinal(left.Label, right.Label));
        return shares;
    }

    /// <summary>Network mounts in macOS <c>mount</c> output. The label is the source, not a password.</summary>
    public static IReadOnlyList<ConnectedShare> ConnectedFromMountOutput(string mountOutput)
    {
        ArgumentNullException.ThrowIfNull(mountOutput);
        var shares = new List<ConnectedShare>();
        foreach (var mount in MacNetworkMounts(mountOutput))
        {
            shares.Add(new ConnectedShare(mount.Source, mount.Point));
        }

        return shares;
    }
}
