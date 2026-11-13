namespace D3skCommander.Core.NetDrives;

/// <summary>
/// The two top levels of a Windows network path: a server root (<c>\\server\</c>, the list of its shares) and a
/// share root (<c>\\server\share</c>). Pure: either separator, any trailing separators; device paths
/// (<c>\\?\</c>, <c>\\.\</c>) are neither.
/// </summary>
public static class UncPath
{
    /// <summary>The server of <c>\\server</c> or <c>\\server\</c>, else null.</summary>
    public static string? Server(string? path)
    {
        var parts = Parts(path);
        return parts is { Length: 1 } ? parts[0] : null;
    }

    /// <summary>The server of a share root <c>\\server\share</c> (with or without a trailing separator), else null.</summary>
    public static string? ShareServer(string? path)
    {
        var parts = Parts(path);
        return parts is { Length: 2 } ? parts[0] : null;
    }

    /// <summary>The canonical server root: <c>\\server\</c>.</summary>
    public static string ServerRoot(string server)
    {
        ArgumentException.ThrowIfNullOrEmpty(server);
        return "\\\\" + server + "\\";
    }

    /// <summary>A share of <paramref name="server"/>: <c>\\server\share</c>.</summary>
    public static string ShareRoot(string server, string share)
    {
        ArgumentException.ThrowIfNullOrEmpty(server);
        ArgumentException.ThrowIfNullOrEmpty(share);
        return "\\\\" + server + "\\" + share;
    }

    /// <summary>
    /// <see cref="Directory.Exists(string)"/>, except that a Windows server root counts as a folder without asking
    /// the network (it has no directory of its own: the panel lists its shares).
    /// </summary>
    public static bool FolderExists(string path) =>
        (OperatingSystem.IsWindows() && Server(path) is not null) || Directory.Exists(path);

    private static string[]? Parts(string? path)
    {
        if (path is null || path.Length < 3 || !IsSeparator(path[0]) || !IsSeparator(path[1]) || IsSeparator(path[2]))
        {
            return null;
        }

        var parts = path[2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] is "?" or ".")
        {
            return null;
        }

        return parts;
    }

    private static bool IsSeparator(char c) => c is '\\' or '/';
}
