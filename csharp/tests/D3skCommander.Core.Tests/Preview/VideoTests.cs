using D3skCommander.Core.Preview;
using Xunit;

namespace D3skCommander.Core.Tests.Preview;

public class VideoTests
{
    [Fact]
    public void WindowsFindsTheRegistryFolderThenTheProgramFolders()
    {
        var registry = Path.Combine("D:", "Apps", "VLC");
        var program = Path.Combine("C:", "Program Files");
        var installed = Path.Combine(program, "VideoLAN", "VLC");
        bool InProgramFiles(string path) => path.StartsWith(installed, StringComparison.Ordinal);

        var found = VlcLocator.Find(VlcPlatform.Windows, InProgramFiles, [registry, program]);

        Assert.Equal(new VlcInstall(installed, null, Path.Combine(installed, "vlc.exe")), found);
        Assert.Equal(registry, VlcLocator.Find(VlcPlatform.Windows, _ => true, [registry, program])!.LibraryFolder);
    }

    [Fact]
    public void MacOSUsesTheBundleLibraryAndNamesItsPlugins()
    {
        var bundle = Path.Combine("Applications", "VLC.app");
        var macos = Path.Combine(bundle, "Contents", "MacOS");

        var found = VlcLocator.Find(VlcPlatform.MacOS, path => path.EndsWith("libvlc.dylib", StringComparison.Ordinal), [null, bundle]);

        Assert.Equal(new VlcInstall(Path.Combine(macos, "lib"), Path.Combine(macos, "plugins"), Path.Combine(macos, "VLC")), found);
    }

    [Fact]
    public void LinuxNeedsTheLibraryAndNoVlcIsNull()
    {
        Assert.Equal("/usr/lib64", VlcLocator.Find(VlcPlatform.Linux, path => path.Contains("lib64", StringComparison.Ordinal), ["/usr/lib/x86_64-linux-gnu", "/usr/lib64"])!.LibraryFolder);
        Assert.Null(VlcLocator.Find(VlcPlatform.Linux, _ => false, ["/usr/lib"]));
        Assert.Null(VlcLocator.Find(VlcPlatform.Windows, _ => false, ["C:"]));
    }

    [Theory]
    [InlineData(640, 360, 640, 360, 2560, 384)]
    [InlineData(3840, 2160, 1920, 1080, 7680, 1088)]
    [InlineData(101, 33, 101, 33, 416, 64)]
    public void FramesShrinkToTheLimitWithPaddedRows(int width, int height, int fitWidth, int fitHeight, int pitch, int lines)
    {
        Assert.Equal(new VideoFrameSize(fitWidth, fitHeight, pitch, lines), VideoFrames.Fit(width, height));
    }

    [Fact]
    public void TheClockShowsHoursOnlyWhenTheVideoHasThem()
    {
        Assert.Equal("0:42 / 3:05", VideoFrames.Clock(TimeSpan.FromSeconds(42), TimeSpan.FromSeconds(185)));
        Assert.Equal("0:02:03 / 1:30:00", VideoFrames.Clock(TimeSpan.FromSeconds(123), TimeSpan.FromMinutes(90)));
        Assert.Equal("0:05", VideoFrames.Clock(TimeSpan.FromSeconds(5), TimeSpan.Zero));
    }

    [Fact]
    public void PlayingStartsAtTheSettingUnlessTheFileIsNotLongerThanThat()
    {
        Assert.Equal(TimeSpan.Zero, VideoFrames.Start(0, TimeSpan.FromMinutes(3)));
        Assert.Equal(TimeSpan.FromSeconds(30), VideoFrames.Start(30, TimeSpan.FromMinutes(3)));
        Assert.Equal(TimeSpan.FromSeconds(30), VideoFrames.Start(30, TimeSpan.Zero));
        Assert.Equal(TimeSpan.Zero, VideoFrames.Start(30, TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.Zero, VideoFrames.Start(-5, TimeSpan.FromMinutes(3)));
    }
}
