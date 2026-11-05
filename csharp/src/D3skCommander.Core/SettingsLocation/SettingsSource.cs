namespace D3skCommander.Core.SettingsLocation;

/// <summary>Why the settings live where they do.</summary>
public enum SettingsSource
{
    /// <summary><c>D3SK_CONFIG_DIR</c>.</summary>
    Env,

    /// <summary><c>prefs.json</c> sits next to the program.</summary>
    Portable,

    /// <summary>A pointer file names the folder.</summary>
    Chosen,

    /// <summary>The platform folder already held settings.</summary>
    Existing,

    /// <summary>Nobody chose yet.</summary>
    Default,
}
