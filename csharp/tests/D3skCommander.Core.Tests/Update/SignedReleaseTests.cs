using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using D3sk.Update.Signature;
using D3sk.Update.UpdateClient;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Update;
using Xunit;
using AppConfig = D3skCommander.Core.Update.UpdateConfig;
using LibraryConfig = D3sk.Update.UpdateClient.UpdateConfig;

namespace D3skCommander.Core.Tests.Update;

/// <summary>A fake GitHub: routes by absolute URL, records every request, answers "no network" for an unknown URL.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private readonly List<string> _seen = [];
    private readonly List<string?> _bearers = [];

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

    public IReadOnlyList<string?> Bearers
    {
        get
        {
            lock (_seen)
            {
                return [.. _bearers];
            }
        }
    }

    public FakeGitHub On(string method, string url, Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        _routes[method + " " + url] = answer;
        return this;
    }

    public FakeGitHub On(string method, string url, HttpStatusCode status, byte[] body) =>
        On(method, url, _ => new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });

    public FakeGitHub On(string method, string url, HttpStatusCode status, string body) =>
        On(method, url, status, Encoding.UTF8.GetBytes(body));

    public HttpClient Client() => new(this);

    public int Count(string method, string url) => Seen.Count(s => s == method + " " + url);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = request.Method.Method + " " + request.RequestUri!.GetLeftPart(UriPartial.Path);
        lock (_seen)
        {
            _seen.Add(key);
            _bearers.Add(request.Headers.Authorization?.Parameter);
        }

        return _routes.TryGetValue(key, out var answer)
            ? Task.FromResult(answer(request))
            : throw new HttpRequestException("no route to " + key);
    }
}

/// <summary>
/// The signed update flow through <see cref="UpdateService"/>, on the real v0.2.1 release list
/// (<c>tests/fixtures/github-api/2022-11-28</c>) with a <c>SHA256SUMS</c> and a signature made by a key pair that
/// exists only inside the test.
/// </summary>
public class SignedReleaseTests
{
    private const string Api = "https://api.github.com/repos/neijix/D3SKCOMMANDER/releases";
    private const string Download = "https://github.com/neijix/D3SKCOMMANDER/releases/download/v0.2.1/";
    private const string PackageName = "d3skcommander-0.2.1-windows-x86_64.zip";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACorrectlySignedReleaseIsOfferedAndDownloaded()
    {
        using var world = new World();
        var check = await world.Service().CheckAsync("0.2.0", Ct);
        Assert.Equal(CheckStatus.Newer, check.Status);
        Assert.Equal("0.2.1", check.Offer!.Version);
        Assert.Equal(PackageName, check.Offer.Asset.Name);

        var (download, result) = await world.RunAsync(check.Offer);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Empty(result.Failed);
        Assert.Null(download.Refusal);
        Assert.Equal(Path.Combine(world.Dir.Path, PackageName), download.Path);
        Assert.Equal(world.Package, File.ReadAllBytes(download.Path!));
    }

    [Fact]
    public async Task AVersionThatIsNotNewerIsUpToDate()
    {
        using var world = new World();
        var check = await world.Service().CheckAsync("0.2.1", Ct);
        Assert.Equal(CheckStatus.UpToDate, check.Status);
        Assert.Null(check.Offer);
    }

    [Fact]
    public async Task AReleaseWithoutThisPlatformsAssetIsNotOffered()
    {
        using var world = new World { AssetFor = version => "d3skcommander-" + version + "-haiku-riscv.tar.gz" };
        var check = await world.Service().CheckAsync("0.2.0", Ct);
        Assert.Equal(CheckStatus.UpToDate, check.Status);
        Assert.Null(check.Offer);
    }

    [Fact]
    public async Task AReleaseWithoutASignatureIsRefusedBeforeThePackageIsFetched()
    {
        using var world = new World { Sign = false };
        await AssertRefusedAsync(world, SumsRefusal.NotSigned, "update.release_not_signed");
    }

    [Fact]
    public async Task AReleaseSignedByAnotherKeyIsRefusedBeforeThePackageIsFetched()
    {
        using var world = new World { SignedWith = ECDsa.Create(ECCurve.NamedCurves.nistP256) };
        await AssertRefusedAsync(world, SumsRefusal.SignatureInvalid, "update.signature_invalid");
    }

    [Fact]
    public async Task TamperedSumsAreRefusedBeforeThePackageIsFetched()
    {
        using var world = new World { TamperSumsAfterSigning = true };
        await AssertRefusedAsync(world, SumsRefusal.SignatureInvalid, "update.signature_invalid");
    }

    [Fact]
    public async Task ATamperedPackageIsRefusedAndNoFileIsLeft()
    {
        using var world = new World { ServedPackage = "tampered"u8.ToArray() };
        var check = await world.Service().CheckAsync("0.2.0", Ct);
        var (download, result) = await world.RunAsync(check.Offer!);

        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Single(result.Failed);
        Assert.Equal(SumsRefusal.HashMismatch, download.Refusal);
        Assert.Equal("update.checksum_mismatch", download.RefusalKey);
        Assert.Null(download.Path);
        Assert.Empty(Directory.GetFileSystemEntries(world.Dir.Path));
    }

    [Fact]
    public async Task TheAppsPinnedKeyRefusesAReleaseSignedByATestKey()
    {
        using var world = new World();
        world.Service();
        var pinned = AppConfig.Create("0.2.0", world.AssetFor);
        using var service = new UpdateService(new JobService(), pinned, world.Github.Client(), new D3sk.Update.Keychain.MemoryKeychain());
        var check = await service.CheckAsync("0.2.0", Ct);
        var download = service.Download(check.Offer!, world.Dir.Path, Ct);
        await download.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Equal(SumsRefusal.SignatureInvalid, download.Refusal);
        Assert.Null(download.Path);
    }

    private static async Task AssertRefusedAsync(World world, SumsRefusal refusal, string key)
    {
        var check = await world.Service().CheckAsync("0.2.0", Ct);
        Assert.Equal(CheckStatus.Newer, check.Status);
        var (download, result) = await world.RunAsync(check.Offer!);

        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Single(result.Failed);
        Assert.Equal(refusal, download.Refusal);
        Assert.Equal(key, download.RefusalKey);
        Assert.Null(download.Path);
        Assert.Empty(Directory.GetFileSystemEntries(world.Dir.Path));
        Assert.DoesNotContain("GET " + Download + PackageName, world.Github.Seen);
        Assert.All(
            world.Github.Seen.Where(s => s.StartsWith("GET " + Download, StringComparison.Ordinal)),
            s => Assert.Contains("SHA256SUMS", s, StringComparison.Ordinal));
    }

    private sealed class World : IDisposable
    {
        private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly List<UpdateService> _services = [];

        public World()
        {
            Dir = new WorkDir();
        }

        public WorkDir Dir { get; }

        public FakeGitHub Github { get; private set; } = new();

        public byte[] Package { get; } = Encoding.ASCII.GetBytes(new string('p', 20_000));

        public byte[]? ServedPackage { get; init; }

        public bool Sign { get; init; } = true;

        public ECDsa? SignedWith { get; init; }

        public bool TamperSumsAfterSigning { get; init; }

        public Func<string, string?> AssetFor { get; init; } = version => "d3skcommander-" + version + "-windows-x86_64.zip";

        public UpdateService Service()
        {
            var sums = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(Package)) + "  " + PackageName + "\n");
            var signature = Sign ? ReleaseSignature.Sign(sums, (SignedWith ?? _signer).ExportPkcs8PrivateKeyPem()) : null;
            if (TamperSumsAfterSigning)
            {
                sums = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData("evil"u8)) + "  " + PackageName + "\n");
            }

            Github = new FakeGitHub()
                .On("GET", Api, HttpStatusCode.OK, ListWithSums(Sign))
                .On("GET", Download + PackageName, HttpStatusCode.OK, ServedPackage ?? Package)
                .On("GET", Download + "SHA256SUMS", HttpStatusCode.OK, sums);
            if (signature is not null)
            {
                Github.On("GET", Download + "SHA256SUMS.sig", HttpStatusCode.OK, signature);
            }

            var pem = _signer.ExportSubjectPublicKeyInfoPem();
            var config = new LibraryConfig(
                AppConfig.Repo, AppConfig.TagPrefix, "d3skcommander/0.2.0", pem,
                D3sk.Update.Signature.ReleaseKey.Fingerprint(pem)!, AppConfig.ClientIdEnvName, AppConfig.KeychainService, AssetFor);
            var service = new UpdateService(new JobService(), config, Github.Client(), new D3sk.Update.Keychain.MemoryKeychain(), setting: _ => null);
            _services.Add(service);
            return service;
        }

        public async Task<(UpdateDownload Download, JobResult Result)> RunAsync(UpdateOffer offer)
        {
            var service = _services[^1];
            var download = service.Download(offer, Dir.Path, Ct);
            var result = await download.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            return (download, result);
        }

        public void Dispose()
        {
            foreach (var service in _services)
            {
                service.Dispose();
            }

            _signer.Dispose();
            SignedWith?.Dispose();
            Dir.Dispose();
        }

        // The recorded v0.2.1 listing: its own (unsigned) SHA256SUMS is replaced by ours, and a .sig is added when signing.
        private static string ListWithSums(bool withSignature)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "github-api", "2022-11-28", "d3skcommander-releases.json");
            var releases = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
            var assets = releases[0]!["assets"]!.AsArray();
            foreach (var old in assets.Where(a => a!["name"]!.GetValue<string>() == "SHA256SUMS").ToList())
            {
                assets.Remove(old);
            }

            assets.Add(Asset("SHA256SUMS", 9001));
            if (withSignature)
            {
                assets.Add(Asset("SHA256SUMS.sig", 9002));
            }

            return releases.ToJsonString();
        }

        private static JsonObject Asset(string name, int id) => new()
        {
            ["name"] = name,
            ["url"] = "https://api.github.com/repos/neijix/D3SKCOMMANDER/releases/assets/" + id,
            ["browser_download_url"] = Download + name,
        };
    }
}
