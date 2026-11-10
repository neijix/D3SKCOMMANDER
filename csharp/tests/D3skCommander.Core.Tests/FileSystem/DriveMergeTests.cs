using D3skCommander.Core.FileSystem;
using D3skCommander.Core.NetDrives;
using Xunit;

namespace D3skCommander.Core.Tests.FileSystem;

public class DriveMergeTests
{
    private const string Unc = @"\\nas\media";

    [Fact]
    public void AShareMappedToADriveLetterIsThatDriveOnceAndMarkedNetwork()
    {
        var volumes = new[] { new DriveEntry(@"C:\", "System", "NTFS", 1, 2), new DriveEntry(@"N:\", @"N:\", "NTFS", 3, 4) };
        var shares = new[] { new ConnectedShare(Unc, "n:") };

        var merged = DriveMerge.Merge(volumes, shares, windows: true);

        Assert.Equal([@"C:\", @"N:\"], merged.Select(d => d.Path));
        Assert.False(merged[0].Network);
        Assert.True(merged[1].Network);
        Assert.Equal(Unc, merged[1].Label);
        Assert.Equal(3UL, merged[1].FreeBytes);
    }

    [Fact]
    public void AShareWithNoVolumeIsAddedAsANetworkDriveAtItsRoot()
    {
        var merged = DriveMerge.Merge([new DriveEntry(@"C:\", "", "NTFS", 1, 2)], [new ConnectedShare(Unc, "Z:")], windows: true);

        Assert.Equal([@"C:\", @"Z:\"], merged.Select(d => d.Path));
        Assert.True(merged[1].Network);
    }

    [Fact]
    public void ANetworkFileSystemOrANetworkVolumeIsNetworkWithoutAShare()
    {
        var volumes = new[]
        {
            new DriveEntry("/", "/", "ext4", 1, 2),
            new DriveEntry("/mnt/nas", "nas", "cifs", 1, 2),
            new DriveEntry(@"M:\", "", "NTFS", 1, 2, Network: true),
        };

        var merged = DriveMerge.Merge(volumes, [], windows: false);

        Assert.Equal([false, true, true], merged.Select(d => d.Network));
    }

    [Fact]
    public void CaseCountsOutsideWindows()
    {
        var merged = DriveMerge.Merge([new DriveEntry("/mnt/NAS", "", "ext4", 1, 2)], [new ConnectedShare("smb://nas/x", "/mnt/nas")], windows: false);

        Assert.Equal(["/mnt/NAS", "/mnt/nas"], merged.Select(d => d.Path));
    }
}
