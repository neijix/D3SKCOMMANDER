using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.NetDrives;

/// <summary>
/// Network drives: parse a share address, ask the OS to mount it, and list what is connected.
/// The password goes to the tool's standard input, or to WNet in memory. The app never saves it; on Windows the
/// user may ask Windows Credential Manager to keep a server login (<see cref="Remember"/>).
/// </summary>
public static partial class NetDrive
{
    private static readonly string[] Schemes = ["smb", "sftp", "ftp", "dav", "davs", "afp", "nfs"];

    /// <summary>
    /// Reads a share address. Without a scheme it is an SMB share
    /// (<c>\\server\share</c>, <c>//server/share</c> or <c>server/share</c>). <c>cifs://</c> is SMB.
    /// </summary>
    public static FileResult<Share> Parse(string input) => Parse(input, serverOnly: false);

    /// <summary>
    /// Same; with <paramref name="serverOnly"/> an SMB address may stop at the server (<c>\\server</c>): the share
    /// name is then empty, and connecting logs in to the whole server (Windows).
    /// </summary>
    public static FileResult<Share> Parse(string input, bool serverOnly)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = input.Trim();
        var schemeSplit = text.IndexOf("://", StringComparison.Ordinal);
        string scheme;
        string rest;
        if (schemeSplit >= 0)
        {
            scheme = text[..schemeSplit].ToLowerInvariant();
            if (scheme == "cifs")
            {
                scheme = "smb";
            }

            if (!Schemes.Contains(scheme, StringComparer.Ordinal))
            {
                return Fail<Share>("unsupported protocol: " + scheme + "://");
            }

            rest = text[(schemeSplit + 3)..];
        }
        else
        {
            scheme = "smb";
            rest = TrimStartSlashes(text).Replace('\\', '/');
        }

        var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return Fail<Share>("missing server name");
        }

        SplitUser(parts[0], out var user, out var server);
        if (server.Length == 0)
        {
            return Fail<Share>("missing server name");
        }

        var name = parts.Length > 1 ? parts[1] : "";
        if (name.Length == 0 && (scheme == "afp" || (scheme == "smb" && !serverOnly)))
        {
            return Fail<Share>("missing share name: \\\\server\\share");
        }

        var sub = parts.Length > 2 ? string.Join('/', parts[2..]) : "";
        return FileResult<Share>.Ok(new Share(scheme, user, server, name, sub));
    }

    /// <summary>The share part of a Windows path: <c>\\server\share</c> for a UNC path, <c>Z:</c> for a drive.</summary>
    public static string? WindowsRoot(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = path.Replace('/', '\\');
        if (normalized.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return UncRoot(normalized);
        }

        if (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
        {
            return char.ToUpperInvariant(normalized[0]) + ":";
        }

        return null;
    }

    /// <summary>A drive letter typed as <c>z</c>, <c>Z:</c> or <c>Z:\</c>. An empty field is no letter.</summary>
    public static FileResult<ParsedDriveLetter> DriveLetter(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim().TrimEnd('\\', '/').TrimEnd(':');
        if (trimmed.Length == 0)
        {
            return FileResult<ParsedDriveLetter>.Ok(new ParsedDriveLetter(null));
        }

        if (trimmed.Length == 1 && char.IsAsciiLetter(trimmed[0]))
        {
            return FileResult<ParsedDriveLetter>.Ok(new ParsedDriveLetter(char.ToUpperInvariant(trimmed[0]) + ":"));
        }

        return Fail<ParsedDriveLetter>("not a drive letter: " + text);
    }

    private static string? UncRoot(string normalized)
    {
        var parts = normalized[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] is "?" or ".")
        {
            return null;
        }

        return "\\\\" + parts[0] + "\\" + parts[1];
    }

    private static void SplitUser(string host, out string? user, out string server)
    {
        var at = host.LastIndexOf('@');
        if (at < 0)
        {
            user = null;
            server = host;
            return;
        }

        user = host[..at];
        server = host[(at + 1)..];
    }

    private static string TrimStartSlashes(string text)
    {
        var index = 0;
        while (index < text.Length && text[index] is '\\' or '/')
        {
            index++;
        }

        return text[index..];
    }

    private static FileResult<T> Fail<T>(string message)
        where T : class =>
        FileResult<T>.Fail(OperationError.FromMessage(message));
}
