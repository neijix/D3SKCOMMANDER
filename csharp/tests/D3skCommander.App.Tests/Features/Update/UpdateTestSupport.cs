using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using D3sk.Update.Keychain;
using D3sk.Update.Signature;
using D3skCommander.App.Features.Update;
using D3skCommander.Core.Install;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Update;
using LibraryConfig = D3sk.Update.UpdateClient.UpdateConfig;

namespace D3skCommander.App.Tests.Features.Update;

internal static class UpdateTestEnvironment
{
    /// <summary>No test reaches the network: the quiet start-up check of a started app stays off.</summary>
#pragma warning disable CA2255 // A test assembly sets the process-wide opt-out before any test.
    [ModuleInitializer]
    internal static void DisableStartupCheck() =>
        Environment.SetEnvironmentVariable(UpdateCommands.DisableVariable, "1");
#pragma warning restore CA2255
}

internal sealed class RecordingRestarter : IUpdateRestarter
{
    public Relaunch? Started { get; private set; }

    public void Restart(Relaunch relaunch) => Started = relaunch;
}

/// <summary>A keychain whose every call fails, like a machine without a secret service.</summary>
internal sealed class BrokenKeychain : IKeychain
{
    public string? Get(string name) => throw new KeychainException("no secret service");

    public void Set(string name, string secret) => throw new KeychainException("no secret service");

    public bool Remove(string name) => throw new KeychainException("no secret service");
}

/// <summary>
/// A fake GitHub serving one release of this app: the listing, <c>SHA256SUMS</c>, its signature (made by a key that
/// exists only in the test) and the package. Records every request.
/// </summary>
internal sealed class FakeReleases : HttpMessageHandler
{
    private const string Api = "https://api.github.com/repos/neijix/D3SKCOMMANDER/releases";
    private const string Download = "https://github.com/neijix/D3SKCOMMANDER/releases/download/";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly List<string> _seen = [];

    public FakeReleases(string version, UpdateEnvironment environment, byte[]? package = null)
    {
        Version = version;
        Environment = environment;
        Package = package ?? "the new program"u8.ToArray();
        AssetName = UpdateChecker.AssetFor(environment, version);
    }

    public string Version { get; }

    public UpdateEnvironment Environment { get; }

    public string AssetName { get; }

    public byte[] Package { get; }

    public bool Sign { get; init; } = true;

    public bool TamperPackage { get; init; }

    /// <summary>When set, every request fails like a machine without network.</summary>
    public bool Offline { get; init; }

    /// <summary>When set, the listing answers 404 like a private repository seen without a token.</summary>
    public bool PrivateRepository { get; init; }

    public IReadOnlyList<string> Seen
    {
        get
        {
            lock (_seen)
            {
                return [.. _seen];
            }
        }
    }

    public UpdateService Service(IKeychain? keychain = null)
    {
        var pem = _key.ExportSubjectPublicKeyInfoPem();
        var config = new LibraryConfig(
            Core.Update.UpdateConfig.Repo, Core.Update.UpdateConfig.TagPrefix, "d3skcommander/" + Environment.Version, pem,
            D3sk.Update.Signature.ReleaseKey.Fingerprint(pem)!, Core.Update.UpdateConfig.ClientIdEnvName, Core.Update.UpdateConfig.KeychainService,
            version => UpdateChecker.AssetFor(Environment, version));
        return new UpdateService(new JobService(), config, new HttpClient(this), keychain ?? new MemoryKeychain(), setting: _ => null);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
        lock (_seen)
        {
            _seen.Add(request.Method.Method + " " + url);
        }

        if (Offline)
        {
            throw new HttpRequestException("no route to " + url);
        }

        var folder = Download + "v" + Version + "/";
        var sums = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(Package)) + "  " + AssetName + "\n");
        return Task.FromResult(url switch
        {
            Api when PrivateRepository => Reply(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}"u8.ToArray()),
            Api => Reply(HttpStatusCode.OK, Encoding.UTF8.GetBytes(Listing())),
            _ when url == folder + AssetName => Reply(HttpStatusCode.OK, TamperPackage ? "tampered"u8.ToArray() : Package),
            _ when url == folder + "SHA256SUMS" => Reply(HttpStatusCode.OK, sums),
            _ when url == folder + "SHA256SUMS.sig" && Sign =>
                Reply(HttpStatusCode.OK, ReleaseSignature.Sign(sums, _key.ExportPkcs8PrivateKeyPem())),
            _ => Reply(HttpStatusCode.NotFound, []),
        });
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, byte[] body) =>
        new(status) { Content = new ByteArrayContent(body) };

    private string Listing()
    {
        var folder = Download + "v" + Version + "/";
        var names = new List<string> { AssetName, "SHA256SUMS" };
        if (Sign)
        {
            names.Add("SHA256SUMS.sig");
        }

        var assets = string.Join(",", names.Select((name, index) =>
            "{\"name\":\"" + name + "\",\"url\":\"https://api.github.com/repos/neijix/D3SKCOMMANDER/releases/assets/" + (100 + index)
            + "\",\"browser_download_url\":\"" + folder + name + "\"}"));
        return "[{\"tag_name\":\"v" + Version + "\",\"draft\":false,\"prerelease\":false,\"assets\":[" + assets + "]}]";
    }
}

internal static class Releases
{
    public static UpdateEnvironment Environment(string version = "0.2.1", InstallKind? kind = null) =>
        new(version, kind ?? new InstallKind.Portable("/opt/d3sk/d3skcommander"), "linux", "x86_64");
}
