using D3skCommander.App.Shell;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

/// <summary>The column sets: which one a panel's tab shows, and the width a resized header column keeps.</summary>
public sealed partial class PanelWorkspace
{
    private List<ColumnSet>? _memorySets;

    /// <summary>Every set the preferences define (the built-ins first); without a settings store, the built-ins.</summary>
    public IReadOnlyList<ColumnSet> ColumnSets =>
        _settings?.Preferences.Current.AllColumnSets()
        ?? _memorySets
        ?? ColumnSetCatalog.Effective(null, Columns.Default);

    /// <summary>The set the active tab of <paramref name="side"/> shows (an unknown name is the default set).</summary>
    public ColumnSet ColumnSetFor(PanelSide side) => ColumnSetCatalog.Resolve(ColumnSets, Read(side).ColumnSet);

    /// <summary>The name of the set the active panel shows.</summary>
    public string ActiveColumnSetName => ColumnSetFor(_active).Name;

    /// <summary>Makes the active tab of <paramref name="side"/> show the set called <paramref name="name"/>.</summary>
    public void UseColumnSet(PanelSide side, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var tab = TabOf(side);
        if (tab is null)
        {
            return;
        }

        var set = ColumnSetCatalog.Resolve(ColumnSets, name);
        if (tab.ColumnSetName == set.Name)
        {
            return;
        }

        tab.ColumnSetName = set.Name;
        Publish(side, announce: false);
    }

    /// <summary>Cycles the active tab to the next set.</summary>
    public void NextColumnSet()
    {
        var next = ColumnSetCatalog.Next(ColumnSets, ActiveColumnSetName);
        UseColumnSet(_active, next.Name);
    }

    /// <summary>Keeps <paramref name="width"/> as the width of <paramref name="id"/> in the set called <paramref name="setName"/> (saved after a short delay).</summary>
    public void SaveColumnWidth(string setName, ColumnId id, double width)
    {
        var sets = ColumnSets.Select(set => set.Clone()).ToList();
        var target = sets.FirstOrDefault(set => string.Equals(set.Name, setName, StringComparison.OrdinalIgnoreCase));
        var column = target?.Columns.FirstOrDefault(c => c.Id == id);
        if (column is null)
        {
            return;
        }

        column.Width = width;
        if (_settings is null)
        {
            _memorySets = sets;
            return;
        }

        var preferences = _settings.Preferences.Current;
        preferences.ColumnSets = sets;
        _settings.Preferences.Save(preferences);
    }
}
