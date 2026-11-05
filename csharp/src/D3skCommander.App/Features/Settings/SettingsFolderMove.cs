using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.App.Features.Settings;

/// <summary>Moves the settings files to another folder and points the next start at it.</summary>
public static class SettingsFolderMove
{
    /// <summary>
    /// Moves what <paramref name="from"/> holds to <paramref name="to"/> (files already there are kept) and
    /// writes the pointer file. Throws the file system's own exceptions; the caller reports them.
    /// </summary>
    public static void Run(string from, string to, SettingsPlaces places)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentNullException.ThrowIfNull(places);
        SettingsDirectory.MoveFiles(from, to);
        SettingsDirectory.WritePointer(places.PointerHome(), to);
    }

    /// <summary>
    /// The first launch asks once: nobody chose a folder, no preferences exist, and neither
    /// <c>D3SK_CONFIG_DIR</c> nor the screenshot hook is in play.
    /// </summary>
    public static bool ShouldAskFirstLaunch(SettingsFolder folder, bool screenshotHookActive)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return !screenshotHookActive && folder.Source != SettingsSource.Env && folder.NeedsChoice();
    }
}
