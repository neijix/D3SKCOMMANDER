namespace D3skCommander.Core.Search;

/// <summary>Search forms saved under a name (case-sensitive). Persisted by <see cref="SearchTemplateStore"/>.</summary>
public sealed class SearchTemplates
{
    /// <summary>The forms by name. Public for the JSON file; use the methods.</summary>
    public Dictionary<string, SearchForm> Items { get; set; } = [];

    /// <summary>Saves a copy of the form, replacing a template of the same name. A blank name is ignored.</summary>
    public void Save(string name, SearchForm form)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(form);
        if (!string.IsNullOrWhiteSpace(name))
        {
            Items[name.Trim()] = form.Clone();
        }
    }

    public bool Contains(string name) => name is not null && Items.ContainsKey(name.Trim());

    /// <summary>A copy of the saved form, or null when there is none of that name.</summary>
    public SearchForm? Load(string name) =>
        name is not null && Items.TryGetValue(name.Trim(), out var form) && form is not null ? form.Clone() : null;

    public bool Delete(string name) => name is not null && Items.Remove(name.Trim());

    /// <summary>The names, sorted without regard to case.</summary>
    public IReadOnlyList<string> Names() =>
        Items.Keys.Order(StringComparer.OrdinalIgnoreCase).ThenBy(static n => n, StringComparer.Ordinal).ToArray();
}
