using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.NetDrives;

/// <summary>A Windows server root has no folder of its own: disk usage scans each of its shares instead.</summary>
public static class ServerScan
{
    /// <summary>
    /// The share roots (<c>\\server\share</c>) to scan for <paramref name="path"/> when it is a server root
    /// (<c>\\server</c> or <c>\\server\</c>); null for any other path. A server that cannot be listed gives its error.
    /// </summary>
    public static FileResult<IReadOnlyList<string>>? ShareRoots(string path, IServerShares shares)
    {
        ArgumentNullException.ThrowIfNull(shares);
        if (UncPath.Server(path) is not { } server)
        {
            return null;
        }

        var listed = shares.Shares(server);
        if (!listed.IsSuccess)
        {
            return FileResult<IReadOnlyList<string>>.Fail(listed.Error!);
        }

        IReadOnlyList<string> roots = [.. listed.Value.Select(share => UncPath.ShareRoot(server, share))];
        return FileResult<IReadOnlyList<string>>.Ok(roots);
    }
}
