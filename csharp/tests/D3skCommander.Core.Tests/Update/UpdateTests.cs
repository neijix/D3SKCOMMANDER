using System.Net;
using D3skCommander.Core.Install;
using D3skCommander.Core.Update;
using Xunit;

namespace D3skCommander.Core.Tests.Update;

/// <summary>The six update tests of v0.1 <c>src/core/update/tests.rs</c> that did not move to install-kind.</summary>
public class UpdateTests
{
    [Fact]
    public void VersionsCompareNumericallyAndReleasesBeatPreReleases()
    {
        Assert.True(ReleaseVersion.IsNewer("0.10.0", "0.9.9"));
        Assert.True(ReleaseVersion.IsNewer("v1.0.0", "0.9.0"));
        Assert.True(ReleaseVersion.IsNewer("1.0.0", "1.0.0-beta.2"));
        Assert.True(ReleaseVersion.IsNewer("1.0.0-rc.1", "1.0.0-beta.2"));
        Assert.False(ReleaseVersion.IsNewer("1.0.0", "1.0.0"));
        Assert.False(ReleaseVersion.IsNewer("0.9.0", "1.0.0"));
        Assert.False(ReleaseVersion.IsNewer("nightly", "1.0.0"));
        Assert.Equal(ReleaseVersion.Parse("2"), ReleaseVersion.Parse("2.0.0"));
        Assert.Null(ReleaseVersion.Parse("1.2.3.4"));
    }

    [Fact]
    public void TheApiUrlComesFromTheRepositoryAddress()
    {
        Assert.Equal(
            "https://api.github.com/repos/neijix/d3skcommander/releases/latest",
            ReleaseUrls.Latest("https://github.com/neijix/d3skcommander/"));
        Assert.Null(ReleaseUrls.Latest("https://gitlab.com/a/b"));
        Assert.Null(ReleaseUrls.Latest("https://github.com/a"));
        Assert.NotNull(ReleaseUrls.Latest(UpdateMetadata.RepositoryUrl));
    }

    [Fact]
    public void ARecordedGitHubReleaseIsRead()
    {
        var parsed = ReleaseParser.Parse(Fixture("ripgrep-latest.json"));
        Assert.True(parsed.IsSuccess, parsed.Error);
        var release = parsed.Value!;
        Assert.Equal("15.2.0", release.Version);
        Assert.StartsWith("https://github.com/BurntSushi/ripgrep/releases/", release.Page);
        Assert.DoesNotContain("\r", release.Notes);
        Assert.Equal(28, release.Assets.Count);
        var asset = release.Assets[0];
        Assert.Equal("ripgrep-15.2.0-aarch64-apple-darwin.tar.gz", asset.Name);
        Assert.StartsWith("https://github.com/", asset.Url);
        Assert.Equal(1_764_284, asset.Size);
        Assert.Equal("3750b2e93f37e0c692657da574d7019a101c0084da05a790c83fd335bad973e4", asset.Sha256);
    }

    [Fact]
    public async Task TheRecordedReleaseIsReadFromTheApiAnswer()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, Fixture("ripgrep-latest.json"));
        using var source = new HttpAssetSource("0.2.0", handler);
        var parsed = await source.ReadLatestAsync(
            "https://api.github.com/repos/BurntSushi/ripgrep/releases/latest",
            TestContext.Current.CancellationToken);
        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.Equal("15.2.0", parsed.Value!.Version);
        var request = handler.Request;
        Assert.NotNull(request);
        Assert.Equal("2022-11-28", Header(request, "X-GitHub-Api-Version"));
        Assert.Equal("application/vnd.github+json", Header(request, "Accept"));
        Assert.Contains("d3skcommander/0.2.0", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingReleaseSaysNoneIsPublished()
    {
        var handler = new ScriptedHandler(HttpStatusCode.NotFound, "");
        using var source = new HttpAssetSource("0.2.0", handler);
        var url = "https://api.github.com/repos/neijix/d3skcommander/releases/latest";
        var parsed = await source.ReadLatestAsync(url, TestContext.Current.CancellationToken);
        Assert.False(parsed.IsSuccess);
        Assert.Contains("no published release at " + url, parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftsAndPreReleasesAreRefused()
    {
        var pre = ReplaceOnce(Fixture("ripgrep-latest.json"), "\"prerelease\":false", "\"prerelease\":true");
        Assert.False(ReleaseParser.Parse(pre).IsSuccess);
        Assert.False(ReleaseParser.Parse("{}").IsSuccess);
    }

    [Fact]
    public void EachInstallKindPicksItsOwnReleaseFile()
    {
        var release = SampleRelease();
        Assert.Equal(
            "d3skcommander-1.0.0-linux-x86_64.deb",
            Name(release, new InstallKind.Deb(""), "linux", "x86_64"));
        Assert.EndsWith(".AppImage", Name(release, new InstallKind.AppImage(""), "linux", "x86_64"), StringComparison.Ordinal);
        Assert.EndsWith("linux-x86_64.tar.gz", Name(release, new InstallKind.Portable(""), "linux", "x86_64"), StringComparison.Ordinal);
        Assert.Equal("d3skcommander-1.0.0-macos-aarch64.tar.gz", Name(release, new InstallKind.MacApp(""), "macos", "aarch64"));
        Assert.EndsWith("-setup.exe", Name(release, new InstallKind.WindowsSetup(""), "windows", "x86_64"), StringComparison.Ordinal);
        Assert.EndsWith(".zip", Name(release, new InstallKind.Portable(""), "windows", "x86_64"), StringComparison.Ordinal);
        Assert.Null(AssetChoice.Pick(release, new InstallKind.Deb(""), "linux", "aarch64"));
        Assert.Null(AssetChoice.Pick(release, new InstallKind.Source(), "linux", "x86_64"));
    }

    [Fact]
    public void EveryInstallKindFindsItsFileInOurOwnRelease()
    {
        var parsed = ReleaseParser.Parse(Fixture("d3skcommander-latest.json"));
        Assert.True(parsed.IsSuccess, parsed.Error);
        var release = parsed.Value!;
        var cases = new (InstallKind Kind, string Os, string Arch)[]
        {
            (new InstallKind.Deb(""), "linux", "x86_64"),
            (new InstallKind.AppImage(""), "linux", "x86_64"),
            (new InstallKind.Portable(""), "linux", "x86_64"),
            (new InstallKind.WindowsSetup(""), "windows", "x86_64"),
            (new InstallKind.Portable(""), "windows", "x86_64"),
        };
        foreach (var (kind, os, arch) in cases)
        {
            var asset = AssetChoice.Pick(release, kind, os, arch);
            Assert.NotNull(asset);
            Assert.Equal(64, asset.Sha256?.Length);
        }

        // The recorded 0.2.1 release predates the macOS tar.gz name (Q6).
        Assert.Null(AssetChoice.Pick(release, new InstallKind.MacApp(""), "macos", "aarch64"));
    }

    private static string Name(GitHubRelease release, InstallKind kind, string os, string arch)
    {
        var asset = AssetChoice.Pick(release, kind, os, arch);
        Assert.NotNull(asset);
        return asset.Name;
    }

    private static GitHubRelease SampleRelease()
    {
        string[] names =
        [
            "d3skcommander-1.0.0-linux-x86_64.deb",
            "d3skcommander-1.0.0-linux-x86_64.AppImage",
            "d3skcommander-1.0.0-linux-x86_64.tar.gz",
            "d3skcommander-1.0.0-macos-aarch64.dmg",
            "d3skcommander-1.0.0-macos-aarch64.tar.gz",
            "d3skcommander-1.0.0-windows-x86_64-setup.exe",
            "d3skcommander-1.0.0-windows-x86_64.zip",
            "SHA256SUMS",
        ];
        var assets = new ReleaseAsset[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            assets[i] = new ReleaseAsset(names[i], "https://example.invalid/" + names[i], 1, null);
        }

        return new GitHubRelease("1.0.0", "", "https://example.invalid/release", assets);
    }

    private static string Fixture(string name)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "github-releases",
            "2022-11-28",
            name);
        return File.ReadAllText(path);
    }

    private static string Header(HttpRequestMessage request, string name)
    {
        Assert.True(request.Headers.TryGetValues(name, out var values));
        return string.Join(",", values);
    }

    private static string ReplaceOnce(string text, string oldValue, string newValue)
    {
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.True(index >= 0);
        return string.Concat(text.AsSpan(0, index), newValue, text.AsSpan(index + oldValue.Length));
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public ScriptedHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
        }
    }
}
