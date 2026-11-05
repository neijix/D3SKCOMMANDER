using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.Core.Settings;

/// <summary>Session file <see cref="SettingsFileNames.Session"/> in the folder it is given.</summary>
public sealed class SessionStore : SettingsStore<Session>
{
    public SessionStore(string folder, TimeProvider? time = null)
        : base(folder, SettingsFileNames.Session, Session.SchemaVersion, time)
    {
    }
}
