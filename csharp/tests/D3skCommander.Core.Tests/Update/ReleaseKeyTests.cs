using D3skCommander.Core.Update;
using Xunit;
using Library = D3sk.Update.Signature.ReleaseKey;
using Pinned = D3skCommander.Core.Update.ReleaseKey;

namespace D3skCommander.Core.Tests.Update;

/// <summary>The pinned release key and the update configuration built on it.</summary>
public class ReleaseKeyTests
{
    [Fact]
    public void PinnedPemIsAUsableKeyWhoseFingerprintIsPinnedToo()
    {
        Assert.Equal(Pinned.Fingerprint, Library.Fingerprint(Pinned.PublicKeyPem));
        Assert.Matches("^[0-9a-f]{64}$", Pinned.Fingerprint);
    }

    [Fact]
    public void ConfigNamesTheRepoTagKeychainAndUserAgent()
    {
        var config = UpdateConfig.Create("1.2.3", _ => null);
        Assert.Equal("neijix/D3SKCOMMANDER", config.Repo);
        Assert.Equal("v", config.TagPrefix);
        Assert.Equal("d3skcommander/1.2.3", config.UserAgent);
        Assert.Equal("D3SKCOMMANDER_GITHUB_CLIENT_ID", config.ClientIdEnvName);
        Assert.Equal("d3sk/d3skcommander", config.KeychainService);
        Assert.Equal(Pinned.PublicKeyPem, config.PublicKeyPem);
        Assert.Equal(Pinned.Fingerprint, config.KeyFingerprint);
        Assert.Equal(D3sk.Update.SignIn.GitHubApp.ClientId, config.ClientId);
    }
}
