using System.Globalization;
using System.Text;

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

    private static string? ShareUri(string kind, string parameters)
    {
        if (kind == "smb-share")
        {
            var server = Parameter(parameters, "server");
            var share = Parameter(parameters, "share");
            if (server is null || share is null)
            {
                return null;
            }

            return "smb://" + server + "/" + share + "/";
        }

        var host = Parameter(parameters, "host");
        if (host is null)
        {
            return null;
        }

        var scheme = kind == "dav" && Parameter(parameters, "ssl") == "true" ? "davs" : kind;
        var user = Parameter(parameters, "user");
        var port = Parameter(parameters, "port");
        var userText = user is null ? "" : user + "@";
        var portText = port is null ? "" : ":" + port;
        return scheme + "://" + userText + host + portText + "/";
    }

    private static string? Parameter(string parameters, string key)
    {
        var prefix = key + "=";
        foreach (var piece in parameters.Split(','))
        {
            if (piece.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Unescape(piece[prefix.Length..]);
            }
        }

        return null;
    }

    private static string Unescape(string text)
    {
        var input = Encoding.UTF8.GetBytes(text);
        var output = new byte[input.Length];
        var written = 0;
        var index = 0;
        while (index < input.Length)
        {
            if (TryPercent(input, index, out var value))
            {
                output[written] = value;
                written++;
                index += 3;
                continue;
            }

            output[written] = input[index];
            written++;
            index++;
        }

        return Encoding.UTF8.GetString(output, 0, written);
    }

    private static bool TryPercent(byte[] input, int index, out byte value)
    {
        value = 0;
        if (input[index] != (byte)'%' || index + 2 >= input.Length)
        {
            return false;
        }

        var hex = Encoding.ASCII.GetString(input, index + 1, 2);
        return byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
