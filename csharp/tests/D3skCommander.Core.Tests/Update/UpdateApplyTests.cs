using D3skCommander.Core.Archives;
using D3skCommander.Core.Install;
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
        Assert.Equal("built from source: update with git pull, then cargo build", result.Error);
        Assert.Equal("source", File.ReadAllText(exe));
    }

    [Fact]
    public void TheWindowsSetupRelaunchesSilently()
    {
        var result = UpdateApply.Install(new InstallKind.WindowsSetup("a.exe"), "s.exe");
        Assert.True(result.IsSuccess, result.Error);
        var relaunch = result.Value!;
        Assert.Equal("s.exe", relaunch.Program);
        Assert.Equal(["/S", "/R"], relaunch.Arguments);
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
}
