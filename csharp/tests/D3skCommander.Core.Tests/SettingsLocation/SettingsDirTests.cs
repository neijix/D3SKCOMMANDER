using System.Text;
using D3skCommander.Core.Install;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.Core.Tests.SettingsLocation;

/// <summary>P5: all 8 tests of v0.1 <c>src/core/settings_dir.rs</c>, with the new file names.</summary>
public class SettingsDirTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void AFirstLaunchProposesTheConfigSubfolderOfTheInstall()
    {
        using var root = new ScratchFolder();
        var places = Places(root.Folder);
        var location = SettingsDirectory.Resolve(places);
        Assert.Equal(Path.Combine(root.Folder, "install", "config"), location.Dir);
        Assert.Equal(SettingsSource.Default, location.Source);
        Assert.True(location.NeedsChoice());

        Directory.CreateDirectory(location.Dir);
        File.WriteAllText(Path.Combine(location.Dir, SettingsFileNames.Preferences), "{}");
        Assert.False(SettingsDirectory.Resolve(places).NeedsChoice());
    }

    [Fact]
    public void WithoutAWritableInstallFolderThePlatformFolderIsUsed()
    {
        using var root = new ScratchFolder();
        var places = Places(root.Folder);
        places.InstallWritable = false;
        Assert.Equal(places.PlatformDir, SettingsDirectory.Resolve(places).Dir);
        Assert.Equal(places.PlatformDir, places.PointerHome());
        places.InstallDir = null;
        Assert.Equal(places.PlatformDir, SettingsDirectory.Resolve(places).Dir);
    }

    [Fact]
    public void ExistingPlatformSettingsWinOverTheDefault()
    {
        using var root = new ScratchFolder();
        var places = Places(root.Folder);
        Directory.CreateDirectory(places.PlatformDir);
        File.WriteAllText(Path.Combine(places.PlatformDir, SettingsFileNames.Preferences), "{}");
        Assert.Equal(
            new SettingsFolder(places.PlatformDir, SettingsSource.Existing),
            SettingsDirectory.Resolve(places));
    }

    [Fact]
    public void APointerWinsOverExistingSettingsAndThePortableMarkerOverBoth()
    {
        using var root = new ScratchFolder();
        var places = Places(root.Folder);
        Directory.CreateDirectory(places.PlatformDir);
        File.WriteAllText(Path.Combine(places.PlatformDir, SettingsFileNames.Preferences), "{}");
        var chosen = Path.Combine(root.Folder, "elsewhere");
        SettingsDirectory.WritePointer(places.PointerHome(), chosen);
        Assert.Equal(chosen, SettingsDirectory.Resolve(places).Dir);
        Assert.Equal(SettingsSource.Chosen, SettingsDirectory.Resolve(places).Source);

        File.WriteAllText(Path.Combine(places.InstallDir!, SettingsFileNames.Preferences), "{}");
        Assert.Equal(SettingsSource.Portable, SettingsDirectory.Resolve(places).Source);

        places.Env = Path.Combine(root.Folder, "env");
        Assert.Equal(Path.Combine(root.Folder, "env"), SettingsDirectory.Resolve(places).Dir);
    }

    [Fact]
    public void APointerInThePlatformFolderIsFoundWithoutAnInstallFolder()
    {
        using var root = new ScratchFolder();
        var places = Places(root.Folder);
        places.InstallDir = null;
        var chosen = Path.Combine(root.Folder, "synced");
        SettingsDirectory.WritePointer(places.PointerHome(), chosen);
        Assert.Equal(chosen, SettingsDirectory.Resolve(places).Dir);
    }

    [Fact]
    public void PointersInsideTheirFolderAreRelativeAndUtf16IsRead()
    {
        using var root = new ScratchFolder();
        var home = root.Folder;
        SettingsDirectory.WritePointer(home, Path.Combine(home, "config"));
        Assert.Equal(Encoding.UTF8.GetBytes("config\n"), File.ReadAllBytes(Path.Combine(home, SettingsFileNames.Pointer)));
        Assert.Equal(Path.Combine(home, "config"), SettingsDirectory.ReadPointer(home));

        // The Windows setup: UTF-16LE with a BOM, CRLF, a non-ASCII path.
        var encoded = Encoding.Unicode.GetBytes("D:\\Réglages\r\n");
        var bytes = new byte[encoded.Length + 2];
        bytes[0] = 0xFF;
        bytes[1] = 0xFE;
        encoded.CopyTo(bytes, 2);
        File.WriteAllBytes(Path.Combine(home, SettingsFileNames.Pointer), bytes);
        Assert.Equal(Path.Combine(home, "D:\\Réglages"), SettingsDirectory.ReadPointer(home));

        File.WriteAllText(Path.Combine(home, SettingsFileNames.Pointer), "\uFEFF  \n", Utf8);
        Assert.Null(SettingsDirectory.ReadPointer(home));
    }

    [Fact]
    public void MovingKeepsWhatTheTargetAlreadyHas()
    {
        using var root = new ScratchFolder();
        var from = Path.Combine(root.Folder, "a");
        var to = Path.Combine(root.Folder, "b");
        Directory.CreateDirectory(from);
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(from, SettingsFileNames.Preferences), "old");
        File.WriteAllText(Path.Combine(from, SettingsFileNames.Vault), "pw");
        File.WriteAllText(Path.Combine(from, SettingsFileNames.Session), "session");
        File.WriteAllText(Path.Combine(from, SettingsFileNames.Hotlist), "hot");
        File.WriteAllText(Path.Combine(from, SettingsFileNames.Sites), "sites");
        File.WriteAllText(Path.Combine(from, SettingsFileNames.OperationsLog), "log");
        var thumb = Path.Combine(from, "cache", "thumbnails");
        Directory.CreateDirectory(thumb);
        File.WriteAllText(Path.Combine(thumb, "a.png"), "img");
        File.WriteAllText(Path.Combine(to, SettingsFileNames.Preferences), "target");

        SettingsDirectory.MoveFiles(from, to);

        Assert.Equal("target", File.ReadAllText(Path.Combine(to, SettingsFileNames.Preferences)));
        Assert.Equal("pw", File.ReadAllText(Path.Combine(to, SettingsFileNames.Vault)));
        Assert.False(File.Exists(Path.Combine(from, SettingsFileNames.Vault)));
        Assert.True(File.Exists(Path.Combine(from, SettingsFileNames.Preferences)));
        Assert.Equal("session", File.ReadAllText(Path.Combine(to, SettingsFileNames.Session)));
        Assert.Equal("hot", File.ReadAllText(Path.Combine(to, SettingsFileNames.Hotlist)));
        Assert.Equal("sites", File.ReadAllText(Path.Combine(to, SettingsFileNames.Sites)));
        Assert.Equal("log", File.ReadAllText(Path.Combine(to, SettingsFileNames.OperationsLog)));
        Assert.False(File.Exists(Path.Combine(from, SettingsFileNames.OperationsLog)));
        Assert.True(File.Exists(Path.Combine(thumb, "a.png")));
        Assert.False(File.Exists(Path.Combine(to, "cache", "thumbnails", "a.png")));

        SettingsDirectory.MoveFiles(to, to);
        Assert.True(File.Exists(Path.Combine(to, SettingsFileNames.Vault)));
    }

    [Fact]
    public void InstallFoldersPerKind()
    {
        var exe = "/x/app/d3skcommander";
        Assert.Equal("/x/app", SettingsDirectory.InstallDirectory(new InstallKind.Portable(exe)));
        Assert.Equal("/x", SettingsDirectory.InstallDirectory(new InstallKind.AppImage("/x/d3sk.AppImage")));
        Assert.Null(SettingsDirectory.InstallDirectory(new InstallKind.Deb(exe)));
        Assert.Null(SettingsDirectory.InstallDirectory(new InstallKind.MacApp("/A/d.app")));
        Assert.Null(SettingsDirectory.InstallDirectory(new InstallKind.Source()));
    }

    private static SettingsPlaces Places(string root)
    {
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(install);
        return new SettingsPlaces
        {
            ExeDir = install,
            InstallWritable = true,
            InstallDir = install,
            PlatformDir = Path.Combine(root, "platform"),
        };
    }

    private sealed class ScratchFolder : IDisposable
    {
        public ScratchFolder()
        {
            Folder = Path.Combine(Path.GetTempPath(), "d3sk-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
        }

        public string Folder { get; }

        public void Dispose()
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
    }
}
