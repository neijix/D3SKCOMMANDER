using D3skCommander.Core.Install;

namespace D3skCommander.Core.Update;

/// <summary>
/// The release file for an <see cref="InstallKind"/>. Names end the way
/// <c>P38</c> names them: <c>d3skcommander-&lt;version&gt;-&lt;os&gt;-&lt;arch&gt;&lt;suffix&gt;</c>.
/// </summary>
public static class AssetChoice
{
    /// <summary>The end of the file name that updates <paramref name="kind"/>. Null for a source build.</summary>
    public static string? Suffix(InstallKind kind, string os, string arch)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(arch);
        if (kind is InstallKind.AppImage)
        {
            return "-linux-" + arch + ".AppImage";
        }

        if (kind is InstallKind.Deb)
        {
            return "-linux-" + arch + ".deb";
        }

        if (kind is InstallKind.MacApp)
        {
            return "-macos-" + arch + ".app.tar.gz";
        }

        if (kind is InstallKind.WindowsSetup)
        {
            return "-windows-" + arch + "-setup.exe";
        }

        if (kind is InstallKind.Portable)
        {
            if (os == "windows")
            {
                return "-windows-" + arch + ".zip";
            }

            return "-" + os + "-" + arch + ".tar.gz";
        }

        return null;
    }

    /// <summary>The first asset whose name ends with <see cref="Suffix"/>.</summary>
    public static ReleaseAsset? Pick(GitHubRelease release, InstallKind kind, string os, string arch)
    {
        ArgumentNullException.ThrowIfNull(release);
        var suffix = Suffix(kind, os, arch);
        if (suffix is null)
        {
            return null;
        }

        foreach (var asset in release.Assets)
        {
            if (asset.Name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return asset;
            }
        }

        return null;
    }
}
