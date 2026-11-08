using D3skCommander.Core.Install;
using Xunit;

namespace D3skCommander.Core.Tests.Install;

/// <summary>Whether a Windows setup update needs administrator rights: the install folder is not writable.</summary>
public class InstallElevationTests
{
    [Fact]
    public void AWritableInstallFolderNeedsNoAdministrator()
    {
        var dir = Directory.CreateTempSubdirectory("d3sk-elev-");
        try
        {
            var kind = new InstallKind.WindowsSetup(Path.Combine(dir.FullName, "d3skcommander.exe"));
            Assert.False(InstallElevation.NeedsAdministrator(kind));
            Assert.Empty(dir.GetFileSystemInfos());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AFolderTheUserCannotWriteNeedsAdministrator()
    {
        var missing = Path.Combine(Path.GetTempPath(), "d3sk-elev-missing-" + Guid.NewGuid().ToString("N"));
        var kind = new InstallKind.WindowsSetup(Path.Combine(missing, "d3skcommander.exe"));
        Assert.True(InstallElevation.NeedsAdministrator(kind));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void OtherInstallKindsNeverAskForAdministrator()
    {
        Assert.False(InstallElevation.NeedsAdministrator(new InstallKind.Source()));
        Assert.False(InstallElevation.NeedsAdministrator(new InstallKind.Portable("/nowhere/d3skcommander")));
    }
}
