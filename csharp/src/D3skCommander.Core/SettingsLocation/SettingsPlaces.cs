using D3skCommander.Core.Install;

namespace D3skCommander.Core.SettingsLocation;

/// <summary>What <see cref="SettingsDirectory.Resolve"/> looks at. <see cref="Current"/> reads this process.</summary>
public sealed class SettingsPlaces
{
    /// <summary>Override variable. Set and not empty, it wins over every other place.</summary>
    public const string ConfigDirVariable = "D3SK_CONFIG_DIR";

    /// <summary><see cref="ConfigDirVariable"/> when set and not empty.</summary>
    public string? Env { get; set; }

    /// <summary>The folder of the running program.</summary>
    public string? ExeDir { get; set; }

    /// <summary>The installation folder, when this install kind has one.</summary>
    public string? InstallDir { get; set; }

    /// <summary>The program may write in <see cref="InstallDir"/>.</summary>
    public bool InstallWritable { get; set; }

    /// <summary>The platform's per-user config folder for the app.</summary>
    public required string PlatformDir { get; set; }

    /// <summary>
    /// Gathers places from the environment, the install kind and the platform folder
    /// (<c>~/.config/d3skcommander</c>, <c>%APPDATA%\d3skcommander</c>, <c>~/Library/Application Support/d3skcommander</c>).
    /// </summary>
    public static SettingsPlaces Current()
    {
        var facts = InstallFacts.Current();
        var install = SettingsDirectory.InstallDirectory(InstallDetection.Detect(facts));
        var exeDir = facts.Exe.Length == 0 ? null : Path.GetDirectoryName(facts.Exe);
        var env = Environment.GetEnvironmentVariable(ConfigDirVariable);
        return new SettingsPlaces
        {
            Env = string.IsNullOrEmpty(env) ? null : env,
            ExeDir = string.IsNullOrEmpty(exeDir) ? null : exeDir,
            InstallDir = install,
            InstallWritable = install is not null && SettingsDirectory.CanWrite(install),
            PlatformDir = PlatformDirectory(),
        };
    }

    /// <summary>Where a new pointer file goes: the install folder when writable, else the platform folder.</summary>
    public string PointerHome() =>
        InstallDir is not null && InstallWritable ? InstallDir : PlatformDir;

    /// <summary>The folder proposed on a first launch.</summary>
    public string DefaultDir() =>
        InstallDir is not null && InstallWritable
            ? Path.Combine(InstallDir, SettingsFileNames.Subfolder)
            : PlatformDir;

    /// <summary><see cref="Environment.SpecialFolder.ApplicationData"/> is the platform config root on each OS.</summary>
    private static string PlatformDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return root.Length == 0 ? "." : Path.Combine(root, "d3skcommander");
    }
}
