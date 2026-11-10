using D3skCommander.App.Features.NetDrives;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Features.Drives;

/// <summary>
/// The drive bar's list: the volumes plus the connected network shares, each drive once and marked network or
/// local (read off the UI thread).
/// </summary>
public sealed class BarDriveSource : IDriveSource
{
    private readonly IDriveSource _volumes;
    private readonly INetDriveRunner _shares;
    private readonly bool _windows;

    public BarDriveSource(IDriveSource volumes, INetDriveRunner shares, bool? windows = null)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(shares);
        _volumes = volumes;
        _shares = shares;
        _windows = windows ?? OperatingSystem.IsWindows();
    }

    public IReadOnlyList<DriveEntry> Mounted() => DriveMerge.Merge(_volumes.Mounted(), _shares.ListConnected(), _windows);
}
