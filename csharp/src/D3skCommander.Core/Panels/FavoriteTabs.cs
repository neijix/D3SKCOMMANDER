using D3skCommander.Core.Settings;

namespace D3skCommander.Core.Panels;

/// <summary>The two panels of a saved set.</summary>
public enum FavoriteSide
{
    Left,
    Right,
}

/// <summary>What restoring a set does with the tabs that are open.</summary>
public enum FavoriteRestoreMode
{
    /// <summary>The set's tabs replace the open ones.</summary>
    Replace,

    /// <summary>The set's tabs come after the open ones.</summary>
    Add,
}

/// <summary>
/// A named set of tabs: both panels' tabs (the session's tab type: folder, sort, view, column set),
/// the active tab of each side and which side was active.
/// </summary>
public sealed class FavoriteTabSet
{
    private string _name = "";
    private SessionPanel _left = new();
    private SessionPanel _right = new();

    public string Name
    {
        get => _name;
        set => _name = value?.Trim() ?? "";
    }

    public FavoriteSide ActiveSide { get; set; }

    public SessionPanel Left
    {
        get => _left;
        set => _left = value ?? new SessionPanel();
    }

    public SessionPanel Right
    {
        get => _right;
        set => _right = value ?? new SessionPanel();
    }

    public FavoriteTabSet Clone() => new()
    {
        Name = Name,
        ActiveSide = ActiveSide,
        Left = Copy(Left),
        Right = Copy(Right),
    };

    private static SessionPanel Copy(SessionPanel panel)
    {
        var copy = new SessionPanel { ActiveIndex = panel.ActiveIndex };
        foreach (var tab in panel.Tabs)
        {
            copy.Tabs.Add(new SessionTab
            {
                Directory = tab.Directory,
                Sort = tab.Sort,
                ViewMode = tab.ViewMode,
                ColumnSet = tab.ColumnSet,
                ColumnWidths = new ColumnWidths
                {
                    Name = tab.ColumnWidths.Name,
                    Extension = tab.ColumnWidths.Extension,
                    Size = tab.ColumnWidths.Size,
                    Date = tab.ColumnWidths.Date,
                    Attributes = tab.ColumnWidths.Attributes,
                },
            });
        }

        return copy;
    }
}

/// <summary>
/// The saved sets of tabs, in the order the menu lists them. Names are unique, compared without case.
/// Persisted by <see cref="FavoriteTabsStore"/>.
/// </summary>
public sealed class FavoriteTabs
{
    private List<FavoriteTabSet> _sets = [];

    /// <summary>The sets in order. Public for the JSON file; a nameless or missing entry is dropped when read.</summary>
    public List<FavoriteTabSet> Sets
    {
        get => _sets;
        set => _sets = Clean(value);
    }

    /// <summary>The set called <paramref name="name"/>, or null.</summary>
    public FavoriteTabSet? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _sets.FirstOrDefault(set => Same(set.Name, name));
    }

    /// <summary>Stores <paramref name="set"/>: replaces the one with its name (keeping its place) or appends. True when it replaced one.</summary>
    public bool Save(FavoriteTabSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(set.Name);
        var index = _sets.FindIndex(other => Same(other.Name, set.Name));
        if (index < 0)
        {
            _sets.Add(set);
            return false;
        }

        _sets[index] = set;
        return true;
    }

    /// <summary>Renames a set. False when it is not found, the new name is blank or another set has it already.</summary>
    public bool Rename(string name, string newName)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(newName);
        var wanted = newName.Trim();
        var set = Find(name);
        if (set is null || wanted.Length == 0)
        {
            return false;
        }

        if (_sets.Any(other => !ReferenceEquals(other, set) && Same(other.Name, wanted)))
        {
            return false;
        }

        set.Name = wanted;
        return true;
    }

    public bool Delete(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _sets.RemoveAll(set => Same(set.Name, name)) > 0;
    }

    /// <summary>Moves a set by <paramref name="delta"/> places (-1 up, 1 down), stopping at the ends. False when nothing moved.</summary>
    public bool Move(string name, int delta)
    {
        ArgumentNullException.ThrowIfNull(name);
        var from = _sets.FindIndex(set => Same(set.Name, name));
        var to = Math.Clamp(from + delta, 0, _sets.Count - 1);
        if (from < 0 || to == from)
        {
            return false;
        }

        var set = _sets[from];
        _sets.RemoveAt(from);
        _sets.Insert(to, set);
        return true;
    }

    public FavoriteTabs Clone() => new() { Sets = [.. _sets.Select(set => set.Clone())] };

    private static List<FavoriteTabSet> Clean(List<FavoriteTabSet>? sets)
    {
        var clean = new List<FavoriteTabSet>();
        foreach (var set in sets ?? [])
        {
            if (set is not null && set.Name.Length > 0 && !clean.Any(other => Same(other.Name, set.Name)))
            {
                clean.Add(set);
            }
        }

        return clean;
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
