using D3skCommander.App.Features.Launch;
using Xunit;

namespace D3skCommander.App.Tests.Features.Launch;

public class ExecutablePathTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("d3sk-exepath-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Dir(string name, params string[] files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(dir, file), "x");
        }

        return dir;
    }

    private static string Join(params string[] entries) => string.Join(Path.PathSeparator, entries);

    [Fact]
    public void ARelativePathEntryIsSkipped()
    {
        // A folder that really exists below the current one, so only the relative entry rule can reject it.
        var relative = "d3sk-rel-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(relative);
        try
        {
            File.WriteAllText(Path.Combine(relative, "tool.exe"), "x");

            Assert.Null(ExecutablePath.Resolve("tool", relative, ".exe", windows: true));
            Assert.Null(ExecutablePath.Resolve("tool.exe", relative, null, windows: false));
        }
        finally
        {
            Directory.Delete(relative, recursive: true);
        }
    }

    [Fact]
    public void ADriveRelativeOrRootedButNotQualifiedEntryIsSkipped()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Drive-relative paths exist on Windows only");
        Dir("bin", "tool.exe");
        var driveRelative = Path.GetPathRoot(_root)![..2] + "bin";
        var rooted = @"\bin";

        Assert.Null(ExecutablePath.Resolve("tool", Join(driveRelative, rooted), ".exe", windows: true));
    }

    [Fact]
    public void AnExtensionlessFileNeverWinsOnWindows()
    {
        var shadow = Dir("shadow", "pwsh");
        var real = Dir("real", "pwsh.exe");

        var found = ExecutablePath.Resolve("pwsh", Join(shadow, real), ".exe;.cmd", windows: true);

        Assert.Equal(Path.Combine(real, "pwsh.exe"), found);
        Assert.Null(ExecutablePath.Resolve("pwsh", shadow, ".exe;.cmd", windows: true));
    }

    [Fact]
    public void TheExtensionsAreTriedInPathextOrderOnEachEntry()
    {
        var dir = Dir("both", "tool.exe", "tool.cmd");

        Assert.Equal(Path.Combine(dir, "tool.cmd"), ExecutablePath.Resolve("tool", dir, ".cmd;.exe", windows: true));
        Assert.Equal(Path.Combine(dir, "tool.exe"), ExecutablePath.Resolve("tool", dir, ".exe;.cmd", windows: true));
    }

    [Fact]
    public void ANameWithAPathextExtensionIsFoundAsIs()
    {
        var dir = Dir("full", "tool.exe");

        Assert.Equal(Path.Combine(dir, "tool.exe"), ExecutablePath.Resolve("tool.exe", dir, ".exe;.cmd", windows: true));
    }

    [Fact]
    public void OffWindowsThePlainNameIsFound()
    {
        var dir = Dir("plain", "tool");

        Assert.Equal(Path.Combine(dir, "tool"), ExecutablePath.Resolve("tool", dir, ".exe", windows: false));
    }
}
