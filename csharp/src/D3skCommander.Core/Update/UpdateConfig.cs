using D3sk.Update.UpdateClient;

namespace D3skCommander.Core.Update;

/// <summary>The settings of D3sk.Update's signed updater for this app.</summary>
public static class UpdateConfig
{
    public const string Repo = "neijix/D3SKCOMMANDER";
    public const string TagPrefix = "v";
    public const string ClientIdEnvName = "D3SKCOMMANDER_GITHUB_CLIENT_ID";
    public const string KeychainService = "d3sk/d3skcommander";

    /// <summary>The library configuration for <paramref name="version"/> (the running version) and a platform asset-name rule.</summary>
    /// <remarks>The GitHub App client ID is the library's shared D3SK Updates app; the environment variable overrides it.</remarks>
    public static D3sk.Update.UpdateClient.UpdateConfig Create(string version, Func<string, string?> assetFor) =>
        new(Repo, TagPrefix, "d3skcommander/" + version, ReleaseKey.PublicKeyPem, ReleaseKey.Fingerprint,
            ClientIdEnvName, KeychainService, assetFor);
}
