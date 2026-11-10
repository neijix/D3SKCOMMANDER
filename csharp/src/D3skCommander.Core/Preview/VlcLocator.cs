using System.Runtime.Versioning;

namespace D3skCommander.Core.Preview;

/// <summary>
/// Where the VLC installed on the machine keeps its library: <see cref="LibraryFolder"/> holds libvlc,
/// <see cref="PluginFolder"/> its plugins when they must be named (macOS), <see cref="Program"/> the player itself.
/// </summary>
public sealed record VlcInstall(string LibraryFolder, string? PluginFolder, string Program);

/// <summary>The operating systems <see cref="VlcLocator"/> knows the VLC layout of.</summary>
public enum VlcPlatform
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>
/// Finds the VLC the user installed, so the preview can play videos without the app shipping its own copy:
/// the folder the installer wrote in the registry or the usual program folders on Windows, the app bundle on
/// macOS, the distribution's libvlc on Linux. Null when VLC is not there (the preview then says to install it).
/// </summary>
public static class VlcLocator
{
    private static readonly string[] LinuxFolders =
    [
        "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/usr/lib", "/usr/local/lib",
    ];

    private static readonly string[] MacBundles = ["/Applications/VLC.app", "~/Applications/VLC.app"];

    /// <summary>The VLC of this machine, or null.</summary>
    public static VlcInstall? Find()
    {
        if (OperatingSystem.IsWindows())
        {
            string?[] folders =
            [
                WindowsInstallDir(),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            ];
            return Find(VlcPlatform.Windows, File.Exists, folders);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? Find(VlcPlatform.MacOS, File.Exists, MacBundles.Select(bundle => bundle.Replace("~", home, StringComparison.Ordinal)).ToArray())
            : Find(VlcPlatform.Linux, File.Exists, LinuxFolders);
    }

    /// <summary>
    /// The first VLC found among <paramref name="places"/>: on Windows the install folder (from the registry) then
    /// the program folders, which hold <c>VideoLAN\VLC</c>; on macOS the app bundles; on Linux the library folders.
    /// </summary>
    public static VlcInstall? Find(VlcPlatform platform, Func<string, bool> exists, IReadOnlyList<string?> places)
    {
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(places);
        foreach (var place in places)
        {
            if (string.IsNullOrEmpty(place))
            {
                continue;
            }

            var found = platform switch
            {
                VlcPlatform.Windows => InWindowsFolder(place, exists) ?? InWindowsFolder(Path.Combine(place, "VideoLAN", "VLC"), exists),
                VlcPlatform.MacOS => InBundle(place, exists),
                _ => InLinuxFolder(place, exists),
            };
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static VlcInstall? InWindowsFolder(string folder, Func<string, bool> exists) =>
        exists(Path.Combine(folder, "libvlc.dll")) && exists(Path.Combine(folder, "libvlccore.dll"))
            ? new VlcInstall(folder, null, Path.Combine(folder, "vlc.exe"))
            : null;

    private static VlcInstall? InBundle(string bundle, Func<string, bool> exists)
    {
        var macos = Path.Combine(bundle, "Contents", "MacOS");
        var lib = Path.Combine(macos, "lib");
        return exists(Path.Combine(lib, "libvlc.dylib"))
            ? new VlcInstall(lib, Path.Combine(macos, "plugins"), Path.Combine(macos, "VLC"))
            : null;
    }

    private static VlcInstall? InLinuxFolder(string folder, Func<string, bool> exists) =>
        exists(Path.Combine(folder, "libvlc.so.5"))
            ? new VlcInstall(folder, null, "vlc")
            : null;

    [SupportedOSPlatform("windows")]
    private static string? WindowsInstallDir()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\VideoLAN\VLC");
            return (key?.GetValue("InstallDir") as string)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
