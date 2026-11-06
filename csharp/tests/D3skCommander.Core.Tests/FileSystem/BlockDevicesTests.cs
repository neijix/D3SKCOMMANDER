using D3skCommander.Core.FileSystem;
using Xunit;

namespace D3skCommander.Core.Tests.FileSystem;

public class BlockDevicesTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "lsblk", "2.41.3", name));

    [Fact]
    public void TheRealOutputIsFlattenedWithItsParents()
    {
        var devices = BlockDevices.ParseLsblk(Fixture("real.json"));

        Assert.Equal(37, devices.Count);
        var root = Assert.Single(devices, d => d.MountPoints.Contains("/"));
        Assert.Equal("part", root.Type);
        Assert.Equal("ext4", root.FsType);
        Assert.StartsWith("/dev/nvme", root.ParentPath, StringComparison.Ordinal);
        Assert.False(root.Removable);
    }

    [Fact]
    public void TheUsbOutputKeepsTheRemovableFlags()
    {
        var devices = BlockDevices.ParseLsblk(Fixture("with-usb.json"));

        var key = Assert.Single(devices, d => d.Name == "sdb1");
        Assert.Equal("/dev/sdb", key.ParentPath);
        Assert.True(key.Removable);
        Assert.True(key.Hotplug);
        Assert.Equal("KEY32", key.Label);
        Assert.Empty(key.MountPoints);
        var backup = Assert.Single(devices, d => d.Name == "sdc1");
        Assert.False(backup.Removable);
        Assert.True(backup.Hotplug);
        Assert.Equal(["/media/neijix/Backup"], backup.MountPoints);
    }

    [Fact]
    public void OnlyAnUnmountedFileSystemIsOffered()
    {
        var names = BlockDevices.Unmounted(BlockDevices.ParseLsblk(Fixture("with-usb.json"))).Select(d => d.Name).ToList();

        Assert.Equal(["sdb1"], names);
        Assert.Empty(BlockDevices.Unmounted(BlockDevices.ParseLsblk(Fixture("real.json"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"blockdevices\": 3}")]
    [InlineData("[1,2")]
    [InlineData("{\"blockdevices\":[1,\"a\",{\"name\":\"x\"}]}")]
    public void MalformedTextGivesNoPartition(string json)
    {
        Assert.DoesNotContain(BlockDevices.ParseLsblk(json), d => d.FsType.Length > 0);
    }

    [Fact]
    public void ARemovableMountGivesItsPartitionAndDisk()
    {
        var devices = BlockDevices.ParseLsblk(Fixture("with-usb.json"));

        Assert.Equal(("/dev/sdc1", "/dev/sdc"), BlockDevices.EjectTargets(devices, "/media/neijix/Backup"));
        Assert.True(BlockDevices.IsEjectable(devices, "/media/neijix/Backup/"));
        var internalDisks = BlockDevices.ParseLsblk(Fixture("real.json"));
        Assert.Null(BlockDevices.EjectTargets(internalDisks, "/"));
        Assert.Null(BlockDevices.EjectTargets(devices, "/nowhere"));
    }
}
