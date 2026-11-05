namespace D3skCommander.Core.Ftp;

/// <summary>
/// One entry of a remote directory listing. A symbolic link is a file: the listing
/// cannot tell what it points at.
/// </summary>
public sealed class RemoteEntry
{
    public RemoteEntry(string name, bool isDirectory, ulong size, DateTimeOffset? modified, string permissions)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(permissions);
        Name = name;
        IsDirectory = isDirectory;
        Size = size;
        Modified = modified;
        Permissions = permissions;
    }

    public string Name { get; }

    public bool IsDirectory { get; }

    public ulong Size { get; }

    public DateTimeOffset? Modified { get; }

    /// <summary><c>drwxr-xr-x</c> from LIST, the octal <c>UNIX.mode</c> from MLSD, empty when unknown.</summary>
    public string Permissions { get; }
}
