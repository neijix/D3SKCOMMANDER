namespace D3skCommander.Core.Search;

/// <summary>
/// The last values typed in the search form's text fields, most recent first, <see cref="Capacity"/> per field.
/// Persisted by <see cref="SearchHistoryStore"/>.
/// </summary>
public sealed class SearchHistory
{
    public const int Capacity = 20;

    public const string Name = "name";

    public const string Text = "text";

    public const string Roots = "roots";

    public const string ExcludeFiles = "excludeFiles";

    public const string ExcludeFolders = "excludeFolders";

    /// <summary>The lists by field name. Public for the JSON file; use <see cref="Add"/> and <see cref="Get"/>.</summary>
    public Dictionary<string, List<string>> Fields { get; set; } = [];

    /// <summary>Puts the value first, dropping an equal one (case-sensitive) and what exceeds the cap. A blank value is ignored.</summary>
    public void Add(string field, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Fields.TryGetValue(field, out var list) || list is null)
        {
            list = [];
            Fields[field] = list;
        }

        list.RemoveAll(existing => existing == value);
        list.Insert(0, value);
        if (list.Count > Capacity)
        {
            list.RemoveRange(Capacity, list.Count - Capacity);
        }
    }

    public IReadOnlyList<string> Get(string field)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        return Fields.TryGetValue(field, out var list) && list is not null
            ? list.Where(static v => !string.IsNullOrWhiteSpace(v)).Take(Capacity).ToArray()
            : [];
    }
}
