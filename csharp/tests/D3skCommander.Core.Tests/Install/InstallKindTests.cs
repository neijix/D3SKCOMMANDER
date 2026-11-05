using D3skCommander.Core.Install;
using Xunit;

namespace D3skCommander.Core.Tests.Install;

/// <summary>P29's install kind, moved here from v0.1 <c>src/core/update/tests.rs</c>.</summary>
public class InstallKindTests
{
    [Fact]
    public void TheInstallKindFollowsWhereTheProgramLives()
    {
        var portable = Facts("linux", "/home/u/Apps/d3skcommander");
        Assert.Equal(new InstallKind.Portable(portable.Exe), InstallDetection.Detect(portable));

        var image = Facts("linux", "/home/u/Apps/d3skcommander");
        image = new InstallFacts
        {
            Os = image.Os,
            Exe = image.Exe,
            AppImage = "/home/u/Apps/d3sk.AppImage",
        };
        Assert.Equal(new InstallKind.AppImage("/home/u/Apps/d3sk.AppImage"), InstallDetection.Detect(image));

        var deb = Facts("linux", "/usr/bin/d3skcommander");
        deb = new InstallFacts { Os = deb.Os, Exe = deb.Exe, DpkgOwned = true };
        Assert.Equal(new InstallKind.Deb(deb.Exe), InstallDetection.Detect(deb));

        var mac = Facts("macos", "/Applications/d3skcommander.app/Contents/MacOS/d3skcommander");
        Assert.Equal(new InstallKind.MacApp("/Applications/d3skcommander.app"), InstallDetection.Detect(mac));

        var windows = Facts("windows", @"C:\Users\u\AppData\Local\d3skcommander\d3skcommander.exe");
        Assert.Equal(new InstallKind.Portable(windows.Exe), InstallDetection.Detect(windows));
        windows = new InstallFacts { Os = windows.Os, Exe = windows.Exe, HasUninstaller = true };
        Assert.Equal(new InstallKind.WindowsSetup(windows.Exe), InstallDetection.Detect(windows));

        var source = new InstallFacts { Os = deb.Os, Exe = deb.Exe, DpkgOwned = true, InCargoTarget = true };
        Assert.Equal(new InstallKind.Source(), InstallDetection.Detect(source));
    }

    private static InstallFacts Facts(string os, string exe) => new() { Os = os, Exe = exe };
}
