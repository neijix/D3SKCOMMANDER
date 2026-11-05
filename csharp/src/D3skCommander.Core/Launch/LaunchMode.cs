namespace D3skCommander.Core.Launch;

/// <summary>v0.1 <c>LaunchMode</c>: one process per file, or one process for the whole selection.</summary>
public enum LaunchMode
{
    OncePerFile,
    AllAtOnce,
}

/// <summary>The OS family v0.1 branched on. Tests pass it in; production uses <see cref="LaunchPlatforms.Current"/>.</summary>
public enum LaunchPlatform
{
    Linux,
    MacOS,
    Windows,
}

/// <summary>The platform this process is running on, using v0.1's Windows / macOS / everything-else split.</summary>
public static class LaunchPlatforms
{
    public static LaunchPlatform Current
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return LaunchPlatform.Windows;
            }

            if (OperatingSystem.IsMacOS())
            {
                return LaunchPlatform.MacOS;
            }

            return LaunchPlatform.Linux;
        }
    }
}
