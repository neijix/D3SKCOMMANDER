using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.Core.Settings;

/// <summary>Hotlist file <see cref="SettingsFileNames.Hotlist"/> in the folder it is given.</summary>
public sealed class HotlistStore : SettingsStore<Hotlist>
{
    public HotlistStore(string folder, TimeProvider? time = null)
        : base(folder, SettingsFileNames.Hotlist, Hotlist.SchemaVersion, time)
    {
    }
}
