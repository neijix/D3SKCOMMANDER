namespace D3skCommander.Core.Ftp;

/// <summary>
/// A saved FTP site. The password is never part of it: with <see cref="SavePassword"/> set it
/// lives in the password store under <see cref="PasswordKey"/> (<see cref="DefaultPasswordKey"/>
/// when that is empty).
/// </summary>
public sealed class FtpSite
{
    public const int DefaultPort = 21;

    private string _name = "";
    private string _host = "";
    private string _user = "";
    private string _remoteDir = "";
    private string _passwordKey = "";

    public string Name
    {
        get => _name;
        set => _name = value ?? "";
    }

    public string Host
    {
        get => _host;
        set => _host = value ?? "";
    }

    /// <summary>0 means <see cref="DefaultPort"/>.</summary>
    public int Port { get; set; }

    /// <summary>Empty is an anonymous login.</summary>
    public string User
    {
        get => _user;
        set => _user = value ?? "";
    }

    /// <summary>Explicit FTPS (<c>AUTH TLS</c>), the server certificate validated.</summary>
    public bool Secure { get; set; }

    /// <summary>Passive data connections. Off means active.</summary>
    public bool Passive { get; set; } = true;

    /// <summary>Folder entered after login; empty stays in the login folder.</summary>
    public string RemoteDir
    {
        get => _remoteDir;
        set => _remoteDir = value ?? "";
    }

    public bool SavePassword { get; set; }

    /// <summary>Key of the password in the password store. Empty means <see cref="DefaultPasswordKey"/>.</summary>
    public string PasswordKey
    {
        get => _passwordKey;
        set => _passwordKey = value ?? "";
    }

    public int EffectivePort => Port <= 0 ? DefaultPort : Port;

    /// <summary>The key the site uses in the password store: <c>ftp:user@host:port</c> (v0.1's).</summary>
    public string DefaultPasswordKey => FormattableString.Invariant($"ftp:{User}@{Host.Trim()}:{EffectivePort}");

    public string EffectivePasswordKey => PasswordKey.Length > 0 ? PasswordKey : DefaultPasswordKey;
}
