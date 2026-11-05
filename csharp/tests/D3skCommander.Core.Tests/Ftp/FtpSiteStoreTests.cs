using D3skCommander.Core.Crypt;
using D3skCommander.Core.Ftp;
using Xunit;

namespace D3skCommander.Core.Tests.Ftp;

public class FtpSiteStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("d3sk-sites-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void SitesRoundTripThroughSitesJson()
    {
        using (var store = new FtpSiteStore(_folder))
        {
            var sites = new FtpSites();
            sites.Items.Add(new FtpSite
            {
                Name = "home",
                Host = "ftp.example.org",
                Port = 2121,
                User = "me",
                Secure = true,
                Passive = false,
                RemoteDir = "/pub",
                SavePassword = true,
                PasswordKey = "ftp:me@ftp.example.org:2121",
            });
            store.Save(sites);
        }

        using var again = new FtpSiteStore(_folder);
        var site = Assert.Single(again.Current.Items);
        Assert.Equal("home", site.Name);
        Assert.Equal("ftp.example.org", site.Host);
        Assert.Equal(2121, site.Port);
        Assert.Equal("me", site.User);
        Assert.True(site.Secure);
        Assert.False(site.Passive);
        Assert.Equal("/pub", site.RemoteDir);
        Assert.True(site.SavePassword);
        Assert.Equal("ftp:me@ftp.example.org:2121", site.PasswordKey);
    }

    [Fact]
    public void TheFileNeverHoldsAPassword()
    {
        using (var store = new FtpSiteStore(_folder))
        {
            var sites = new FtpSites();
            sites.Items.Add(new FtpSite { Host = "h", User = "u", SavePassword = true });
            store.Save(sites);
        }

        var json = File.ReadAllText(Path.Combine(_folder, "sites.json"));
        Assert.DoesNotContain("\"password\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"savePassword\": true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ADefaultSiteIsPassiveOnThePlainPort()
    {
        var site = new FtpSite { Host = "h" };
        Assert.True(site.Passive);
        Assert.Equal(21, site.EffectivePort);
        Assert.False(site.Secure);
        Assert.Equal("ftp:@h:21", site.DefaultPasswordKey);
    }

    [Fact]
    public void ThePasswordComesFromThePasswordStoreUnderTheSiteKey()
    {
        var site = new FtpSite { Host = "ftp.example.org", User = "me", Port = 2121, SavePassword = true };
        var opened = PasswordStore.Open(Path.Combine(_folder, PasswordStore.FileName), "master");
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        var vault = opened.Value;
        vault.Set(site.EffectivePasswordKey, "hunter2");
        Assert.True(vault.Save().IsSuccess);

        var reopened = PasswordStore.Open(Path.Combine(_folder, PasswordStore.FileName), "master");
        Assert.Equal("hunter2", reopened.Value.Get(site.EffectivePasswordKey));
        Assert.Equal("ftp:me@ftp.example.org:2121", site.EffectivePasswordKey);
    }

    [Fact]
    public void AMissingFileIsNoSites()
    {
        using var store = new FtpSiteStore(_folder);
        Assert.Empty(store.Current.Items);
    }
}
