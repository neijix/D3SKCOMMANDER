using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Drives;

/// <summary>What a drive menu line does when chosen.</summary>
public enum DriveMenuKind
{
    Computer,
    Volume,
    Place,
    Share,
    Unmounted,
    Eject,
}

/// <summary>One line: <paramref name="Target"/> is a folder (Volume, Place, Share, Eject) or a device (Unmounted).</summary>
public sealed record DriveMenuItem(DriveMenuKind Kind, string Text, string Target, double? UsedFraction, bool IsCurrent)
{
    /// <summary>Lines of one section sit together; an Eject line stays with its volume.</summary>
    public int Section => Kind == DriveMenuKind.Eject ? (int)DriveMenuKind.Volume : (int)Kind;
}

/// <summary>Builds the drive menu: volumes, places, connected shares, unmounted disks.</summary>
public static class DriveMenu
{
    public static List<DriveMenuItem> Build(
        ILocalizer text,
        string currentPath,
        IReadOnlyList<DriveEntry> volumes,
        IReadOnlyList<UserPlace> places,
        IReadOnlyList<ConnectedShare> shares,
        IReadOnlyList<BlockDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(currentPath);
        var items = new List<DriveMenuItem>
        {
            new(DriveMenuKind.Computer, text.Get("computer.menu"), ComputerAddress.Text, null, ComputerAddress.IsAddress(currentPath)),
        };
        var free = text.Get("drives.free");
        var holding = DriveList.Holding(volumes, currentPath);
        foreach (var drive in volumes)
        {
            double? used = drive.TotalBytes > 0 ? Math.Clamp(1.0 - ((double)drive.FreeBytes / drive.TotalBytes), 0, 1) : null;
            items.Add(new DriveMenuItem(DriveMenuKind.Volume, DriveList.Line(drive, free), drive.Path, used, ReferenceEquals(drive, holding)));
            if (BlockDevices.IsEjectable(devices, drive.Path))
            {
                items.Add(new DriveMenuItem(DriveMenuKind.Eject, text.Get("drives.eject") + " " + DriveList.ShortName(drive.Path), drive.Path, null, false));
            }
        }

        foreach (var place in places)
        {
            items.Add(new DriveMenuItem(DriveMenuKind.Place, text.Get(place.Key), place.Path, null, false));
        }

        var network = text.Get("drives.share");
        foreach (var share in shares)
        {
            if (!items.Exists(item => item.Kind == DriveMenuKind.Volume && string.Equals(item.Target, share.Path, StringComparison.Ordinal)))
            {
                items.Add(new DriveMenuItem(DriveMenuKind.Share, share.Label + " (" + network + ")", share.Path, null, false));
            }
        }

        foreach (var device in BlockDevices.Unmounted(devices))
        {
            var name = device.Label.Length > 0 ? device.Label : device.Name;
            items.Add(new DriveMenuItem(DriveMenuKind.Unmounted, text.Get("drives.mount") + " " + name + " (" + LocalFileSystem.FormatSize(device.Size) + ")", device.Path, null, false));
        }

        return items;
    }
}
