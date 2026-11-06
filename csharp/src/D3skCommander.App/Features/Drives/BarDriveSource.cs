using D3skCommander.App.Features.NetDrives;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Features.Drives;

/// <summary>The drive bar's list: the volumes plus the connected network shares (read off the UI thread).</summary>
public sealed class BarDriveSource : IDriveSource
{
    private readonly IDriveSource _volumes;
    private readonly INetDriveRunner _shares;

    public BarDriveSource(IDriveSource volumes, INetDriveRunner shares)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(shares);
        _volumes = volumes;
        _shares = shares;
    }

    public IReadOnlyList<DriveEntry> Mounted()
    {
        var list = new List<DriveEntry>(_volumes.Mounted());
        foreach (var share in _shares.ListConnected())
        {
            if (!list.Exists(drive => string.Equals(drive.Path, share.Path, StringComparison.Ordinal)))
            {
                list.Add(new DriveEntry(share.Path, share.Label, "", 0, 0));
            }
        }

        return list;
    }
}
