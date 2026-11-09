using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.NetDrives;
using D3skCommander.App.Startup;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;

namespace D3skCommander.App.Features.Computer;

/// <summary>
/// The Computer view's sources on this machine. Every call reads again and is synchronous: the panels list
/// through <c>Task.Run</c>, so none of it runs on the UI thread.
/// </summary>
public sealed class OsComputerSources : IComputerSources
{
    private readonly IDriveSource _drives;
    private readonly INetDriveRunner _shares;
    private readonly AppSettings _settings;

    public OsComputerSources(IDriveSource drives, INetDriveRunner shares, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(settings);
        _drives = drives;
        _shares = shares;
        _settings = settings;
    }

    public IReadOnlyList<DriveEntry> MountedDrives() => _drives.Mounted();

    public IReadOnlyList<ConnectedShare> ConnectedShares() => _shares.ListConnected();

    public IReadOnlyList<UserPlace> Places()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return UserPlaces.Standard(home, UserPlaces.ReadUserDirs(home), Directory.Exists);
    }

    public IReadOnlyList<HotlistItem> Favorites() => [.. _settings.Hotlist.Current.Items];

    public IReadOnlyList<string> MountPoints()
    {
        try
        {
            return [.. DriveInfo.GetDrives().Select(drive => drive.Name)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return [];
        }
    }
}
