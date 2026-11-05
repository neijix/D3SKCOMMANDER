using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.Core.Ftp;

/// <summary>The content of <c>sites.json</c>: the saved sites. No site is the default.</summary>
public sealed class FtpSites
{
    public const int SchemaVersion = 1;

    private List<FtpSite> _items = [];

    public List<FtpSite> Items
    {
        get => _items;
        set => _items = value ?? [];
    }
}

/// <summary>Site file <see cref="SettingsFileNames.Sites"/> in the folder it is given. Holds no password.</summary>
public sealed class FtpSiteStore : SettingsStore<FtpSites>
{
    public FtpSiteStore(string folder, TimeProvider? time = null)
        : base(folder, SettingsFileNames.Sites, FtpSites.SchemaVersion, time)
    {
    }
}
