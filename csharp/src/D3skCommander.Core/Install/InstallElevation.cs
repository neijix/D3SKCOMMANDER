using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.Core.Install;

/// <summary>Decides whether updating an install needs administrator rights.</summary>
public static class InstallElevation
{
    /// <summary>
    /// True for a Windows setup install whose folder the current user cannot write
    /// (the same <c>SettingsDirectory.CanWrite</c> probe the settings folder uses; no file is left behind).
    /// </summary>
    public static bool NeedsAdministrator(InstallKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (kind is not InstallKind.WindowsSetup setup)
        {
            return false;
        }

        var folder = Path.GetDirectoryName(setup.Path);
        return string.IsNullOrEmpty(folder) || !SettingsDirectory.CanWrite(folder);
    }
}
