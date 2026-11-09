using D3skCommander.Core.FileSystem;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Settings;

namespace D3skCommander.Core.Providers;

/// <summary>
/// Where the Computer view reads its rows. Each call reads the OS or the settings again; nothing is
/// cached. A call may throw: the provider leaves that group out and lists the rest.
/// </summary>
public interface IComputerSources
{
    /// <summary>The mounted drives, as the drive menu lists them.</summary>
    IReadOnlyList<DriveEntry> MountedDrives();

    /// <summary>The connected network drives.</summary>
    IReadOnlyList<ConnectedShare> ConnectedShares();

    /// <summary>The user's places (Home, Desktop, Documents...).</summary>
    IReadOnlyList<UserPlace> Places();

    /// <summary>The hotlist.</summary>
    IReadOnlyList<HotlistItem> Favorites();

    /// <summary>Every mount point the OS reports, pseudo, system and network mounts included.</summary>
    IReadOnlyList<string> MountPoints();
}
