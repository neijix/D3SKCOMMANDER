using System.Text.Json;

namespace D3skCommander.Core.Localization;

/// <summary>
/// One en/fr table. Embedded files under <c>Localization/</c> are JSON objects of
/// <c>"key": { "en": "...", "fr": "..." }</c>. A key that lacks either language, or a repeated key, is rejected.
/// </summary>
public sealed class StringCatalog
{
    private readonly Dictionary<string, (string En, string Fr)> _entries;

    private StringCatalog(Dictionary<string, (string En, string Fr)> entries) => _entries = entries;

    public IReadOnlyCollection<string> Keys => _entries.Keys;

    public static StringCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A localization catalog must be a JSON object of keys.");
        }

        var entries = new Dictionary<string, (string En, string Fr)>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var (english, french) = ReadPair(property);
            if (!entries.TryAdd(property.Name, (english, french)))
            {
                throw new InvalidOperationException($"duplicate i18n key: {property.Name}");
            }
        }

        return new StringCatalog(entries);
    }

    public static StringCatalog Merge(IEnumerable<StringCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        var combined = new Dictionary<string, (string En, string Fr)>(StringComparer.Ordinal);
        foreach (var catalog in catalogs)
        {
            foreach (var (key, row) in catalog._entries)
            {
                if (!combined.TryAdd(key, row))
                {
                    throw new InvalidOperationException($"duplicate i18n key: {key}");
                }
            }
        }

        return new StringCatalog(combined);
    }

    public bool TryGet(string key, Language language, out string text)
    {
        if (!_entries.TryGetValue(key, out var row))
        {
            text = "";
            return false;
        }

        text = language == Language.Fr ? row.Fr : row.En;
        return true;
    }

    private static (string En, string Fr) ReadPair(JsonProperty property)
    {
        if (property.Value.ValueKind != JsonValueKind.Object
            || !property.Value.TryGetProperty("en", out var english)
            || english.ValueKind != JsonValueKind.String
            || !property.Value.TryGetProperty("fr", out var french)
            || french.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(english.GetString())
            || string.IsNullOrEmpty(french.GetString()))
        {
            throw new FormatException($"i18n key \"{property.Name}\" needs both en and fr");
        }

        return (english.GetString()!, french.GetString()!);
    }
}
