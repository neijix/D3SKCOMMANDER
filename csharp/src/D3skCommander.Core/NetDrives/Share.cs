namespace D3skCommander.Core.NetDrives;

/// <summary>
/// A remote folder the user typed: <c>\\server\share\sub</c>, <c>//server/share</c>,
/// <c>smb://user@server/share</c>, <c>sftp://server/path</c>.
/// </summary>
public sealed record Share(string Scheme, string? User, string Server, string Name, string Sub)
{
    /// <summary>The share itself, without the sub-folder. <c>smb://server/share/</c>.</summary>
    public string Uri()
    {
        var user = User is null ? "" : User + "@";
        if (Name.Length == 0)
        {
            return Scheme + "://" + user + Server + "/";
        }

        return Scheme + "://" + user + Server + "/" + Name + "/";
    }

    /// <summary>The Windows UNC path of the share: <c>\\server\share</c>.</summary>
    public string Unc() => "\\\\" + Server + "\\" + Name;

    /// <summary><paramref name="mount"/> joined with the sub-folder that was typed.</summary>
    public string Inside(string mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        var path = mount;
        foreach (var part in Sub.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, part);
        }

        return path;
    }
}

/// <summary>
/// What the user typed to log in. Any field may be empty (guest, or the current login).
/// The password is never written to a store and never placed on a command line.
/// </summary>
public sealed class Credentials
{
    public Credentials(string user, string domain, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(password);
        User = user;
        Domain = domain;
        Password = password;
    }

    public string User { get; }

    public string Domain { get; }

    public string Password { get; }

    public static Credentials None { get; } = new("", "", "");

    public override string ToString() => "Credentials { User = " + User + ", Domain = " + Domain + " }";
}

/// <summary>One line of the macOS <c>mount</c> command that is a network file system.</summary>
public sealed record MacMount(string Source, string Point, string FsType);

/// <summary>A share the OS has mounted. <see cref="Label"/> is the address; the password is not here.</summary>
public sealed record ConnectedShare(string Label, string Path);

/// <summary>
/// How a mount is asked of the OS. On Linux and macOS the password is only in
/// <see cref="StandardInput"/>. On Windows there is no process: WNet receives the password in memory.
/// </summary>
public sealed record MountCommand(string Program, IReadOnlyList<string> Arguments, string StandardInput);

/// <summary>A drive letter typed as <c>z</c>, <c>Z:</c> or <c>Z:\</c>. <see cref="Letter"/> is null when the field was empty.</summary>
public sealed record ParsedDriveLetter(string? Letter);
