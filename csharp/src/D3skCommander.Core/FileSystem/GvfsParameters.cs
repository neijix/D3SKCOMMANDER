using System.Globalization;
using System.Text;

namespace D3skCommander.Core.FileSystem;

/// <summary>GVFS mount-name parameters, shared by the drive list and network drives.</summary>
internal static class GvfsParameters
{
    public static string? Uri(string kind, string parameters)
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

    internal static string Unescape(string text)
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
