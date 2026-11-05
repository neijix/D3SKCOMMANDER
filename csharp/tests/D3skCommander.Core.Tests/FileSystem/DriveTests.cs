using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.Core.Tests.FileSystem;

public class DriveTests
{
    [Theory]
    [InlineData("/proc", "proc", false)]
    [InlineData("/sys", "sysfs", false)]
    [InlineData("/run/user/1000", "tmpfs", false)]
    [InlineData("/var/lib/docker/overlay2/x", "overlay", false)]
    [InlineData("/snap/core/123", "squashfs", false)]
    [InlineData("/snap/core/123", "ext4", false)]
    [InlineData("/boot/efi", "vfat", false)]
    [InlineData("/", "ext4", true)]
    [InlineData("/home", "btrfs", true)]
    [InlineData("/run/media/anna/USB", "vfat", true)]
    [InlineData("/mnt/nas", "cifs", true)]
    [InlineData("C:\\", "NTFS", true)]
    [InlineData("/Volumes/Backup", "apfs", true)]
    [InlineData("/snapshots", "ext4", true)]
    public void PseudoFileSystemsAreHidden(string mount, string format, bool shown)
    {
        Assert.Equal(shown, DriveList.IsUseful(mount, format));
    }

    [Fact]
    public void MountedListsTheRootAndNoPseudoFileSystem()
    {
        var drives = DriveList.Mounted();
        Assert.NotEmpty(drives);
        Assert.DoesNotContain(drives, drive => drive.Format is "proc" or "sysfs" or "tmpfs" or "overlay");
        Assert.All(drives, drive => Assert.True(drive.TotalBytes >= drive.FreeBytes || drive.TotalBytes == 0));
    }

    [Fact]
    public void ALineShowsLabelPathAndFreeSpace()
    {
        var usb = new DriveEntry("/run/media/anna/USB", "USB", "vfat", 2048, 4096);
        Assert.Equal("USB (/run/media/anna/USB) - 2.0 KB free", DriveList.Line(usb, "free"));
        var root = new DriveEntry("/", "/", "ext4", 0, 0);
        Assert.Equal("/", DriveList.Line(root, "free"));
    }

    [Fact]
    public void PropertiesOfAFileHaveSizeTimesAndAttributes()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(path, "12345");
        var props = EntryProperties.Read(path).Value;
        Assert.Equal("a.txt", props.Name);
        Assert.Equal(path, props.FullPath);
        Assert.Equal(EntryKind.File, props.Kind);
        Assert.Equal(5UL, props.Size);
        Assert.NotNull(props.Modified);
        Assert.Null(props.LinkTarget);
        Assert.NotEmpty(props.Attributes);
    }

    [Fact]
    public void PropertiesOfALinkNameItsTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempFolder();
        var target = Path.Combine(dir.Path, "real.txt");
        File.WriteAllText(target, "x");
        var link = Path.Combine(dir.Path, "link.txt");
        File.CreateSymbolicLink(link, target);
        var props = EntryProperties.Read(link).Value;
        Assert.Equal(EntryKind.Link, props.Kind);
        Assert.Equal(target, props.LinkTarget);
    }

    [Fact]
    public void PropertyLinesAreLocalizedInBothLanguages()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(path, "12345");
        var props = EntryProperties.Read(path).Value;
        var en = EntryPropertiesText.ForEntry(Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En), props, null);
        var fr = EntryPropertiesText.ForEntry(Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr), props, null);
        Assert.Contains("Name: a.txt", en);
        Assert.Contains("Nom: a.txt", fr);
        Assert.Contains("Size: 5 B (5 bytes)", en);
        Assert.Contains("Taille: 5 B (5 octets)", fr);
    }

    [Fact]
    public void AMissingPathIsNotFound()
    {
        using var dir = new TempFolder();
        var result = EntryProperties.Read(Path.Combine(dir.Path, "nope"));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public void ASelectionIsMeasuredWithItsFolders()
    {
        using var dir = new TempFolder();
        var folder = Path.Combine(dir.Path, "d");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "sub", "one"), "123");
        File.WriteAllText(Path.Combine(dir.Path, "two"), "12");
        var size = EntryProperties.Measure([folder, Path.Combine(dir.Path, "two")], CancellationToken.None);
        Assert.Equal(5UL, size.Bytes);
        Assert.Equal(2UL, size.Files);
        Assert.Equal(2UL, size.Folders);
    }

    [Fact]
    public void ACancelledMeasureStopsEarly()
    {
        using var dir = new TempFolder();
        File.WriteAllText(Path.Combine(dir.Path, "a"), "123");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var size = EntryProperties.Measure([Path.Combine(dir.Path, "a")], cancel.Token);
        Assert.Equal(0UL, size.Files);
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = Directory.CreateTempSubdirectory("d3sk-props-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
