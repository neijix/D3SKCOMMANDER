namespace D3skCommander.Core.SettingsLocation;

/// <summary>
/// File names in the settings folder. None reuse a v0.1 name.
/// <see cref="Preferences"/> next to the program is also the portable marker.
/// The pointer keeps the name <see cref="Pointer"/>.
/// </summary>
public static class SettingsFileNames
{
    public const string Preferences = "prefs.json";

    public const string Session = "session.json";

    public const string Hotlist = "hotlist.json";

    public const string SearchHistory = "search-history.json";

    public const string SearchTemplates = "search-templates.json";

    public const string Sites = "sites.json";

    public const string Vault = "vault.d3enc";

    public const string OperationsLog = "ops.log";

    public const string Pointer = "config-location.txt";

    /// <summary>Default folder inside a writable install folder.</summary>
    public const string Subfolder = "config";

    /// <summary>
    /// Files a move takes with the folder. <c>cache/thumbnails</c> is absent: it is rebuilt.
    /// </summary>
    public static IReadOnlyList<string> MovedWithTheFolder { get; } =
    [
        Preferences,
        Session,
        Hotlist,
        SearchHistory,
        SearchTemplates,
        Sites,
        Vault,
        OperationsLog,
    ];
}
