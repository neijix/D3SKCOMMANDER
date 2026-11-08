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
        Assert.Equal(new InstallKind.MacApp(Native("/Applications/d3skcommander.app")), InstallDetection.Detect(mac));

        var windows = Facts("windows", @"C:\Users\u\AppData\Local\d3skcommander\d3skcommander.exe");
        Assert.Equal(new InstallKind.Portable(windows.Exe), InstallDetection.Detect(windows));
        windows = new InstallFacts { Os = windows.Os, Exe = windows.Exe, HasUninstaller = true };
        Assert.Equal(new InstallKind.WindowsSetup(windows.Exe), InstallDetection.Detect(windows));

        var source = new InstallFacts { Os = deb.Os, Exe = deb.Exe, DpkgOwned = true, InSourceTree = true };
        Assert.Equal(new InstallKind.Source(), InstallDetection.Detect(source));
    }

    /// <summary><c>Path.GetDirectoryName</c> rewrites separators on Windows: compare in the host form.</summary>
    private static string Native(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private static InstallFacts Facts(string os, string exe) => new() { Os = os, Exe = exe };

    [Fact]
    public void AFolderHoldingTheInstallRecordIsALinuxScriptInstall()
    {
        var script = new InstallFacts
        {
            Os = "linux",
            Exe = "/home/u/apps/d3skcommander/D3skCommander.App",
            HasInstallRecord = true,
        };
        Assert.Equal(
            new InstallKind.LinuxScript(Native("/home/u/apps/d3skcommander")),
            InstallDetection.Detect(script));
    }

    [Fact]
    public void ASourceTreeWinsOverAnInstallRecord()
    {
        var facts = new InstallFacts { Os = "linux", Exe = "/src/csharp/bin/app", HasInstallRecord = true, InSourceTree = true };
        Assert.Equal(new InstallKind.Source(), InstallDetection.Detect(facts));
    }

    [Fact]
    public void TheFactsSeeTheSolutionAndTheInstallRecordAroundTheProgram()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-kind-");
        try
        {
            var bin = Path.Combine(root.FullName, "csharp", "src", "App", "bin");
            Directory.CreateDirectory(bin);
            Assert.False(InstallFacts.InSourceTreeAt(Path.Combine(bin, "app")));
            File.WriteAllText(Path.Combine(root.FullName, "csharp", "D3skCommander.sln"), "");
            Assert.True(InstallFacts.InSourceTreeAt(Path.Combine(bin, "app")));

            var installed = Path.Combine(root.FullName, "installed");
            Directory.CreateDirectory(installed);
            Assert.False(InstallFacts.HasInstallRecordAt(Path.Combine(installed, "app")));
            File.WriteAllText(Path.Combine(installed, ".d3skcommander-install"), "kind=linux-script\nversion=0.2.1\n");
            Assert.True(InstallFacts.HasInstallRecordAt(Path.Combine(installed, "app")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
