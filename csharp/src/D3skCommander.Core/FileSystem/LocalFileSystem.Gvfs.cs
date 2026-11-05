namespace D3skCommander.Core.FileSystem;

/// <summary>One volume the drive buttons offer. Gvfs shares leave total and free at 0.</summary>
public sealed record Drive(string Label, string Path, ulong Total, ulong Free);

public static partial class LocalFileSystem
{
    /// <summary>
    /// Network shares GVFS mounted under <paramref name="gvfsDirectory"/>, labelled by address.
    /// A missing folder is an empty list. The mount-name grammar is v0.1's <c>gvfs_uri</c>.
    /// </summary>
    public static IReadOnlyList<Drive> GvfsDrives(string gvfsDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(gvfsDirectory);
        var read = ReadChildren(gvfsDirectory);
        if (!read.IsSuccess)
        {
            return [];
        }

        var shares = new List<Drive>();
        foreach (var child in read.Value)
        {
            var label = ShareLabel(child);
            if (label is null)
            {
                continue;
            }

            shares.Add(new Drive(label, child, 0, 0));
        }

        return shares.OrderBy(share => share.Label, StringComparer.Ordinal).ToList();
    }

    private static string? ShareLabel(string path)
    {
        var mount = MountName(path);
        if (mount is null)
        {
            return null;
        }

        var colon = mount.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        var uri = ShareUri(mount[..colon], mount[(colon + 1)..]);
        if (uri is null)
        {
            return null;
        }

        return uri.TrimEnd('/');
    }

    private static string? MountName(string path)
    {
        var parts = Components(path);
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] == "gvfs" && i + 1 < parts.Count)
            {
                return parts[i + 1];
            }
        }

        return null;
    }

    private static List<string> Components(string path)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i <= path.Length; i++)
        {
            if (i != path.Length && path[i] != '/' && path[i] != '\\')
            {
                continue;
            }

            if (i > start)
            {
                parts.Add(path[start..i]);
            }

            start = i + 1;
        }

        return parts;
    }

    private static string? ShareUri(string kind, string parameters) => GvfsParameters.Uri(kind, parameters);
}
