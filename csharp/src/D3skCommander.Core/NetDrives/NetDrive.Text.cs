using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.NetDrives;

public static partial class NetDrive
{
    /// <summary>Quotes <paramref name="text"/> as an AppleScript string literal.</summary>
    public static string AppleScriptString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var escaped = text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    /// <summary>
    /// The AppleScript that mounts <paramref name="share"/> on macOS. The script, password included,
    /// is written to <c>osascript</c>'s standard input. Without a user, macOS asks for the login itself.
    /// </summary>
    public static string MacMountScript(Share share, Credentials credentials)
    {
        ArgumentNullException.ThrowIfNull(share);
        ArgumentNullException.ThrowIfNull(credentials);
        var spoken = share;
        if (share.User is null && credentials.User.Length > 0)
        {
            var user = credentials.Domain.Length == 0 ? credentials.User : credentials.Domain + ";" + credentials.User;
            spoken = share with { User = user };
        }

        var script = "mount volume " + AppleScriptString(WebDavUrl(spoken.Uri()));
        if (credentials.Password.Length > 0)
        {
            script += " with password " + AppleScriptString(credentials.Password);
        }

        return script;
    }

    /// <summary>
    /// The answers <c>gio mount</c> reads on its standard input: user, domain, password, one per line.
    /// An empty line takes gio's default.
    /// </summary>
    public static string GioAnswers(Credentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return credentials.User + "\n" + credentials.Domain + "\n" + credentials.Password + "\n";
    }

    /// <summary>The <c>local path:</c> line of <c>gio info</c>, where GVFS shows the mounted share.</summary>
    public static string? GioLocalPath(string infoOutput)
    {
        ArgumentNullException.ThrowIfNull(infoOutput);
        foreach (var line in Lines(infoOutput))
        {
            const string prefix = "local path: ";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// The URI of the GVFS mount a local path belongs to. A path under
    /// <c>…/gvfs/smb-share:server=nas,share=media/…</c> gives <c>smb://nas/media/</c>.
    /// </summary>
    public static string? GvfsUri(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var parts = Components(path);
        var index = parts.IndexOf("gvfs");
        if (index < 0 || index + 1 >= parts.Count)
        {
            return null;
        }

        var mount = parts[index + 1];
        var colon = mount.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        return UriFromMount(mount[..colon], mount[(colon + 1)..]);
    }

    private static string WebDavUrl(string uri)
    {
        if (uri.StartsWith("dav://", StringComparison.Ordinal))
        {
            return "http://" + uri["dav://".Length..];
        }

        if (uri.StartsWith("davs://", StringComparison.Ordinal))
        {
            return "https://" + uri["davs://".Length..];
        }

        return uri;
    }

    private static string? UriFromMount(string kind, string parameters) => GvfsParameters.Uri(kind, parameters);

    private static List<string> Components(string path)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i <= path.Length; i++)
        {
            if (i != path.Length && path[i] is not ('/' or '\\'))
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

    private static IEnumerable<string> Lines(string text)
    {
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '\n')
            {
                continue;
            }

            if (i == text.Length && start == text.Length)
            {
                yield break;
            }

            var length = i - start;
            if (length > 0 && text[start + length - 1] == '\r')
            {
                length--;
            }

            yield return text.Substring(start, length);
            start = i + 1;
        }
    }
}
