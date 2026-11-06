namespace D3skCommander.Core.Panels;

/// <summary>
/// The column sets a user sees: the built-in "Default" and "Details" first, then their own.
/// "Default" comes from the v0.1 <see cref="Columns"/> flags until the user edits it.
/// </summary>
public static class ColumnSetCatalog
{
    public const string DefaultName = "Default";

    public const string DetailsName = "Details";

    public static bool IsBuiltIn(string name) =>
        Same(name, DefaultName) || Same(name, DetailsName);

    /// <summary>Today's columns: the four of v0.1, each when its flag is on.</summary>
    public static ColumnSet FromLegacy(Columns columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var set = new ColumnSet { Name = DefaultName };
        if (columns.Extension)
        {
            set.Columns.Add(new ColumnSetColumn(ColumnId.Extension));
        }

        if (columns.Size)
        {
            set.Columns.Add(new ColumnSetColumn(ColumnId.Size));
        }

        if (columns.Date)
        {
            set.Columns.Add(new ColumnSetColumn(ColumnId.Modified));
        }

        if (columns.Attributes)
        {
            set.Columns.Add(new ColumnSetColumn(ColumnId.Attributes));
        }

        return set;
    }

    /// <summary>Every column.</summary>
    public static ColumnSet Details() =>
        new(DetailsName, ColumnInfo.Optional.Select(id => new ColumnSetColumn(id)));

    /// <summary>
    /// The sets to use: stored ones cleaned up (a name once, columns once), "Default" and "Details" first
    /// (made from <paramref name="legacy"/> and the built-in list when not stored), then the others in stored order.
    /// </summary>
    public static IReadOnlyList<ColumnSet> Effective(IEnumerable<ColumnSet>? stored, Columns legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ColumnSet? defaults = null;
        ColumnSet? details = null;
        var others = new List<ColumnSet>();
        foreach (var set in stored ?? [])
        {
            var clean = set.Normalized();
            if (clean.Name.Trim().Length == 0)
            {
                continue;
            }

            if (Same(clean.Name, DefaultName))
            {
                defaults ??= Named(clean, DefaultName);
            }
            else if (Same(clean.Name, DetailsName))
            {
                details ??= Named(clean, DetailsName);
            }
            else if (!others.Any(other => Same(other.Name, clean.Name)))
            {
                others.Add(clean);
            }
        }

        var all = new List<ColumnSet> { defaults ?? FromLegacy(legacy), details ?? Details() };
        all.AddRange(others);
        return all;
    }

    /// <summary>The set called <paramref name="name"/>; an unknown or empty name gives "Default" (the first set).</summary>
    public static ColumnSet Resolve(IReadOnlyList<ColumnSet> sets, string? name)
    {
        ArgumentNullException.ThrowIfNull(sets);
        foreach (var set in sets)
        {
            if (name is not null && Same(set.Name, name))
            {
                return set;
            }
        }

        return sets.FirstOrDefault(set => Same(set.Name, DefaultName)) ?? FromLegacy(Columns.Default);
    }

    /// <summary>The set after <paramref name="name"/>, wrapping round (an unknown name counts as "Default").</summary>
    public static ColumnSet Next(IReadOnlyList<ColumnSet> sets, string? name)
    {
        ArgumentNullException.ThrowIfNull(sets);
        var current = Resolve(sets, name);
        var index = 0;
        for (var i = 0; i < sets.Count; i++)
        {
            if (ReferenceEquals(sets[i], current))
            {
                index = i;
            }
        }

        return sets.Count == 0 ? current : sets[(index + 1) % sets.Count];
    }

    /// <summary>A name not used by <paramref name="sets"/>: <paramref name="wanted"/>, else "wanted (2)", "wanted (3)", ...</summary>
    public static string UniqueName(IEnumerable<string> existing, string wanted)
    {
        var names = existing.ToList();
        var name = wanted.Trim();
        if (name.Length == 0)
        {
            name = "Columns";
        }

        var candidate = name;
        for (var n = 2; names.Any(other => Same(other, candidate)); n++)
        {
            candidate = $"{name} ({n})";
        }

        return candidate;
    }

    private static ColumnSet Named(ColumnSet set, string name) => new(name, set.Columns);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
