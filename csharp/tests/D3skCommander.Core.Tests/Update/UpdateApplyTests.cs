using D3skCommander.Core.Archives;
using D3skCommander.Core.Install;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Update;
using Xunit;

namespace D3skCommander.Core.Tests.Update;

/// <summary>The four apply tests of v0.1 <c>src/core/update/apply.rs</c>, plus a hermetic <c>pkexec</c> check.</summary>
public class UpdateApplyTests
{
    [Fact]
    public void ReplaceFileSwapsTheContentAndLeavesNoStagingFile()
    {
        using var dir = new WorkDir();
        var target = Path.Combine(dir.Path, "app");
        var incoming = Path.Combine(dir.Path, "new");
        File.WriteAllText(target, "old");
        File.WriteAllText(incoming, "new");
        FileSwap.Replace(incoming, target);
        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(FileSwap.Sibling(target, ".update-new")));
        FileSwap.RemoveLeftovers(target);
        Assert.False(File.Exists(FileSwap.Sibling(target, ".old")));
    }

    [Fact]
    public void APortableUpdateTakesTheProgramFromTheArchive()
    {
        using var dir = new WorkDir();
        var exe = Path.Combine(dir.Path, "bin", "d3skcommander");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "v1");
        var stage = Path.Combine(dir.Path, "stage");
        Directory.CreateDirectory(stage);
        var payload = Path.Combine(stage, "d3skcommander");
        File.WriteAllText(payload, "v2");
        var archive = Path.Combine(dir.Path, "u.tar.gz");
        var created = ArchiveIo.Create(
            archive,
            ArchiveKind.TarGz,
            [payload],
            6,
            null,
            TestContext.Current.CancellationToken);
        Assert.True(created.IsSuccess, created.Error?.Message);

        var result = UpdateApply.Install(new InstallKind.Portable(exe), archive);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Relaunch.Of(exe), result.Value);
        Assert.Equal("v2", File.ReadAllText(exe));
        Assert.False(Directory.Exists(FileSwap.ScratchDirectory(exe)));
    }

    [Fact]
    public void SourceBuildsAreNotReplaced()
    {
        using var dir = new WorkDir();
        var exe = Path.Combine(dir.Path, "app");
        File.WriteAllText(exe, "source");
        var result = UpdateApply.Install(new InstallKind.Source(), exe);
        Assert.False(result.IsSuccess);
        Assert.Equal("built from source: update with git pull, then rebuild", result.Error);
        Assert.Equal("source", File.ReadAllText(exe));
    }

    [Fact]
    public void TheWindowsSetupRelaunchesSilentlyWithTheInstallFolderLast()
    {
        var exe = Path.Combine(Path.GetTempPath(), "Program Files", "D3SK Commander", "d3skcommander.exe");
        var result = UpdateApply.Install(new InstallKind.WindowsSetup(exe), "s.exe");
        Assert.True(result.IsSuccess, result.Error);
        var relaunch = result.Value!;
        Assert.Equal("s.exe", relaunch.Program);
        Assert.Equal(["/S", "/R", "/D=" + Path.GetDirectoryName(exe)], relaunch.Arguments);
    }

    [Fact]
    public void AMacBundleIsSwappedForTheOneInTheArchive()
    {
        using var dir = new WorkDir();
        var bundle = Path.Combine(dir.Path, "d3skcommander.app");
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(bundle, "Contents", "MacOS", "d3skcommander"), "v1");
        var staged = Path.Combine(dir.Path, "stage", "d3skcommander.app", "Contents", "MacOS");
        Directory.CreateDirectory(staged);
        File.WriteAllText(Path.Combine(staged, "d3skcommander"), "v2");
        var archive = Path.Combine(dir.Path, "app.tar.gz");
        var root = Path.Combine(dir.Path, "stage", "d3skcommander.app");
        var created = ArchiveIo.Create(archive, ArchiveKind.TarGz, [root], 6, null, TestContext.Current.CancellationToken);
        Assert.True(created.IsSuccess, created.Error?.Message);

        var result = UpdateApply.Install(new InstallKind.MacApp(bundle), archive);
        Assert.True(result.IsSuccess, result.Error);
        var relaunch = result.Value;
        Assert.NotNull(relaunch);
        Assert.Equal("open", relaunch.Program);
        Assert.Equal(["-n", bundle], relaunch.Arguments);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(bundle, "Contents", "MacOS", "d3skcommander")));
        Assert.False(Directory.Exists(FileSwap.Sibling(bundle, ".old")));
        Assert.False(Directory.Exists(FileSwap.ScratchDirectory(bundle)));
    }

    [Fact]
    public void ADebInstallUsesPkexec()
    {
        var process = new RecordingProcess(0);
        var result = UpdateApply.Install(new InstallKind.Deb("/usr/bin/d3skcommander"), "/tmp/pkg.deb", process);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("pkexec", process.Program);
        Assert.Equal(["/usr/bin/dpkg", "-i", "/tmp/pkg.deb"], process.Arguments);
        Assert.Equal(Relaunch.Of("/usr/bin/d3skcommander"), result.Value);
    }

    [Fact]
    public void ARefusedDebInstallNamesTheManualCommand()
    {
        var process = new RecordingProcess(126);
        var result = UpdateApply.Install(new InstallKind.Deb("/usr/bin/d3skcommander"), "/tmp/pkg.deb", process);
        Assert.False(result.IsSuccess);
        Assert.Contains("sudo dpkg -i \"/tmp/pkg.deb\"", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinuxScriptInstallIsReplacedInPlaceAndItsRecordGetsTheNewVersion()
    {
        using var dir = new WorkDir();
        var folder = Path.Combine(dir.Path, "apps", "d3skcommander");
        Directory.CreateDirectory(Path.Combine(folder, "config"));
        File.WriteAllText(Path.Combine(folder, "D3skCommander"), "v1");
        File.WriteAllText(Path.Combine(folder, "stale.dll"), "gone");
        File.WriteAllText(Path.Combine(folder, "config", "prefs.json"), "{}");
        File.WriteAllText(Path.Combine(folder, InstallRecord.FileName), "kind=linux-script\nversion=0.2.0\ndesktop=/x/d.desktop\n");
        var archive = LinuxArchive(dir.Path, "v2");

        var result = UpdateApply.Install(new InstallKind.LinuxScript(folder), archive, version: "0.3.1");
        Assert.True(result.IsSuccess, result.Error);
        var app = Path.Combine(folder, "D3skCommander");
        Assert.Equal(Relaunch.Of(app), result.Value);
        Assert.Equal("v2", File.ReadAllText(app));
        Assert.False(File.Exists(Path.Combine(folder, "stale.dll")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(folder, "config", "prefs.json")));
        Assert.Equal(
            ["kind=linux-script", "version=0.3.1", "desktop=/x/d.desktop"],
            File.ReadAllLines(Path.Combine(folder, InstallRecord.FileName)));
        Assert.False(Directory.Exists(FileSwap.Sibling(folder, ".old")));
        Assert.False(Directory.Exists(FileSwap.ScratchDirectory(folder)));
    }

    [Fact]
    public void ALinuxScriptFolderTheSwapCannotWriteFailsAndStaysUntouched()
    {
        using var dir = new WorkDir();
        var folder = Path.Combine(dir.Path, "d3skcommander");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "D3skCommander"), "v1");
        Block(folder);
        var result = UpdateApply.Install(new InstallKind.LinuxScript(folder), LinuxArchive(dir.Path, "v2"), version: "0.3.1");
        Assert.False(result.IsSuccess);
        Assert.Equal(
            "Cannot update: " + folder + " is not writable. Move the app to a folder you own, or reinstall.",
            result.Error);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(folder, "D3skCommander")));
    }

    [Fact]
    public void AMacBundleFolderTheSwapCannotWriteFailsAndStaysUntouched()
    {
        using var dir = new WorkDir();
        var bundle = Path.Combine(dir.Path, "d3skcommander.app");
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(bundle, "Contents", "MacOS", "d3skcommander"), "v1");
        Block(dir.Path);
        var result = UpdateApply.Install(new InstallKind.MacApp(bundle), Path.Combine(dir.Path, "none.tar.gz"));
        Assert.False(result.IsSuccess);
        Assert.StartsWith("Cannot update: " + bundle + " is not writable.", result.Error, StringComparison.Ordinal);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(bundle, "Contents", "MacOS", "d3skcommander")));
    }

    [Fact]
    public void TheNotWritableTextExistsInBothLanguages()
    {
        var en = Localizer.Load(Language.En);
        var fr = Localizer.Load(Language.Fr);
        Assert.Contains("{folder}", en.Get("update.not_writable"), StringComparison.Ordinal);
        Assert.Contains("{folder}", fr.Get("update.not_writable"), StringComparison.Ordinal);
        Assert.NotEqual(en.Get("update.not_writable"), fr.Get("update.not_writable"));
    }

    [Fact]
    public void TheMacAndLinuxScriptInstallsPickTheTarGz()
    {
        Assert.Equal("-macos-aarch64.tar.gz", AssetChoice.Suffix(new InstallKind.MacApp("/A/d.app"), "macos", "aarch64"));
        Assert.Equal("-linux-x86_64.tar.gz", AssetChoice.Suffix(new InstallKind.LinuxScript("/x"), "linux", "x86_64"));
    }

    /// <summary>A folder named like the write probe makes the probe fail on every OS, whatever the account may do.</summary>
    private static void Block(string folder) => Directory.CreateDirectory(Path.Combine(folder, ".d3sk-write-test"));

    private static string LinuxArchive(string root, string content)
    {
        var stage = Path.Combine(root, "stage", "d3skcommander");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "D3skCommander"), content);
        var archive = Path.Combine(root, "linux.tar.gz");
        var created = ArchiveIo.Create(archive, ArchiveKind.TarGz, [stage], 6, null, TestContext.Current.CancellationToken);
        Assert.True(created.IsSuccess, created.Error?.Message);
        return archive;
    }
}
