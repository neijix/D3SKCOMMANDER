using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.NetDrives;

public static partial class NetDrive
{
    /// <summary>The network mounts listed by the macOS <c>mount</c> command.</summary>
    public static IReadOnlyList<MacMount> MacNetworkMounts(string mountOutput)
    {
        ArgumentNullException.ThrowIfNull(mountOutput);
        var mounts = new List<MacMount>();
        foreach (var line in Lines(mountOutput))
        {
            var parsed = ParseMacLine(line);
            if (parsed is not null)
            {
                mounts.Add(parsed);
            }
        }

        return mounts;
    }

    /// <summary>Where macOS mounted <paramref name="share"/>, from the <c>mount</c> command's output.</summary>
    public static string? MacMountPoint(string mountOutput, Share share)
    {
        ArgumentNullException.ThrowIfNull(mountOutput);
        ArgumentNullException.ThrowIfNull(share);
        var server = share.Server.ToLowerInvariant();
        var name = share.Name.ToLowerInvariant();
        foreach (var mount in MacNetworkMounts(mountOutput))
        {
            if (Matches(mount.Source, server, name))
            {
                return mount.Point;
            }
        }

        return null;
    }

    /// <summary>The network mount that contains <paramref name="path"/> (the deepest one).</summary>
    public static string? MacMountContaining(string mountOutput, string path)
    {
        ArgumentNullException.ThrowIfNull(mountOutput);
        ArgumentNullException.ThrowIfNull(path);
        string? best = null;
        var bestCount = -1;
        foreach (var mount in MacNetworkMounts(mountOutput))
        {
            var count = Components(mount.Point).Count;
            if (count > bestCount && IsPrefix(mount.Point, path))
            {
                best = mount.Point;
                bestCount = count;
            }
        }

        return best;
    }

    private static MacMount? ParseMacLine(string line)
    {
        var on = line.IndexOf(" on ", StringComparison.Ordinal);
        if (on < 0)
        {
            return null;
        }

        var rest = line[(on + 4)..];
        var paren = rest.LastIndexOf(" (", StringComparison.Ordinal);
        if (paren < 0)
        {
            return null;
        }

        var fsType = FileSystemType(rest[(paren + 2)..]);
        if (fsType is not ("smbfs" or "afpfs" or "nfs" or "webdav"))
        {
            return null;
        }

        return new MacMount(line[..on], rest[..paren], fsType);
    }

    private static string FileSystemType(string options)
    {
        var end = options.IndexOfAny([',', ')']);
        return (end < 0 ? options : options[..end]).Trim();
    }

    private static bool Matches(string source, string server, string name)
    {
        var lower = source.ToLowerInvariant();
        var host = HostOf(lower);
        var hostMatches = host == server || host.StartsWith(server + ".", StringComparison.Ordinal);
        var nameMatches = name.Length == 0 || GvfsParameters.Unescape(TailOf(lower)).Equals(name, StringComparison.OrdinalIgnoreCase);
        return hostMatches && nameMatches;
    }

    private static string HostOf(string sourceLower)
    {
        var trimmed = sourceLower.TrimStart('/');
        var slash = trimmed.IndexOf('/');
        var first = slash < 0 ? trimmed : trimmed[..slash];
        var at = first.LastIndexOf('@');
        return at < 0 ? first : first[(at + 1)..];
    }

    private static string TailOf(string sourceLower)
    {
        var slash = sourceLower.LastIndexOf('/');
        return slash < 0 ? sourceLower : sourceLower[(slash + 1)..];
    }

    private static bool IsPrefix(string root, string path)
    {
        var rootParts = Components(root);
        var pathParts = Components(path);
        if (rootParts.Count > pathParts.Count)
        {
            return false;
        }

        for (var i = 0; i < rootParts.Count; i++)
        {
            if (!string.Equals(rootParts[i], pathParts[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
