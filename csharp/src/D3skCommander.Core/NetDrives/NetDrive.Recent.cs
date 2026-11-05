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
