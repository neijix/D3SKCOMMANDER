using D3sk.Update.Versioning;
using D3skCommander.Core.Install;
using D3skCommander.Core.Update;
using Xunit;

namespace D3skCommander.Core.Tests.Update;

/// <summary>Version ordering (D3sk.Update's <see cref="SemVer"/>) and the release file each install kind picks.</summary>
public class UpdateTests
{
    [Fact]
    public void VersionsCompareNumericallyAndReleasesBeatPreReleases()
    {
        Assert.Equal(1, SemVer.Compare("0.10.0", "0.9.9"));
        Assert.Equal(1, SemVer.Compare("1.0.0", "0.9.0"));
        Assert.Equal(1, SemVer.Compare("1.0.0", "1.0.0-beta.2"));
        Assert.Equal(1, SemVer.Compare("1.0.0-rc.1", "1.0.0-beta.2"));
        Assert.Equal(0, SemVer.Compare("1.0.0", "1.0.0"));
        Assert.Equal(0, SemVer.Compare("1.0.0+build.5", "1.0.0"));
        Assert.Equal(-1, SemVer.Compare("0.9.0", "1.0.0"));
        Assert.False(SemVer.IsValid("nightly"));
        Assert.False(SemVer.IsValid("1.2.3.4"));
        Assert.True(SemVer.IsPrerelease("1.0.0-beta.2"));
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
}
