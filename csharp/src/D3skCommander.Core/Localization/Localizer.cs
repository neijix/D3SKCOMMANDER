using System.Reflection;
using System.Text;

namespace D3skCommander.Core.Localization;

/// <summary>Serves the embedded en/fr catalogs. Language comes from the settings, otherwise the system.</summary>
public sealed class Localizer : ILocalizer
{
    private readonly StringCatalog _catalog;
    private Language _language;

    public Localizer(StringCatalog catalog, Language language)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        Use(language);
    }

    public Language Language => _language;

    public IReadOnlyCollection<string> Keys => _catalog.Keys;

    public static Localizer Load(Language? settingsLanguage = null, Language? systemLanguage = null, Assembly? assembly = null)
    {
        assembly ??= typeof(Localizer).Assembly;
        var catalogs = new List<StringCatalog>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!IsCatalog(name))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            catalogs.Add(StringCatalog.Parse(reader.ReadToEnd()));
        }

        if (catalogs.Count == 0)
        {
            throw new InvalidOperationException(
                "No embedded localization catalog in " + assembly.GetName().Name
                + ". Resources: " + string.Join(", ", assembly.GetManifestResourceNames()));
        }

        return new Localizer(StringCatalog.Merge(catalogs), Languages.Choose(settingsLanguage, systemLanguage));
    }

    public void Use(Language language)
    {
        if (language is not (Language.En or Language.Fr))
        {
            throw new ArgumentOutOfRangeException(nameof(language), language, "Only English and French are supported.");
        }

        _language = language;
    }

    public string Get(string key) => Lookup(key, _language);

    public string Get(string key, Language language)
    {
        if (language is not (Language.En or Language.Fr))
        {
            throw new ArgumentOutOfRangeException(nameof(language), language, "Only English and French are supported.");
        }

        return Lookup(key, language);
    }

    public string Text(string english, string french)
    {
        ArgumentNullException.ThrowIfNull(english);
        ArgumentNullException.ThrowIfNull(french);
        return _language == Language.Fr ? french : english;
    }

    private string Lookup(string key, Language language)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _catalog.TryGet(key, language, out var text) ? text : key;
    }

    private static bool IsCatalog(string name) =>
        name.Contains("Localization", StringComparison.Ordinal)
        && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
}
