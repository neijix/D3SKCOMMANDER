using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.Core.Settings;

/// <summary>Preferences file <see cref="SettingsFileNames.Preferences"/> in the folder it is given.</summary>
public sealed class PreferencesStore : SettingsStore<Preferences>
{
    public PreferencesStore(string folder, TimeProvider? time = null)
        : base(folder, SettingsFileNames.Preferences, Preferences.SchemaVersion, time)
    {
    }
}
