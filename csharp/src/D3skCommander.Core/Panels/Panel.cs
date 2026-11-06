using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Panels;

/// <summary>One file panel: its tabs and which tab is active. No view.</summary>
public sealed class Panel
{
    private readonly List<Tab> _tabs;
    private readonly IFileSystemProvider _provider;

    private Panel(List<Tab> tabs, int activeTab, IFileSystemProvider provider)
    {
        _tabs = tabs;
        _provider = provider;
        ActiveTab = activeTab;
    }

    public int ActiveTab { get; private set; }

    public Tab Active => _tabs[ActiveTab];

    public IReadOnlyList<Tab> Tabs => _tabs;

    public static async Task<Panel> OpenAsync(
        IReadOnlyList<string> paths,
        int active,
        SortOrder sort,
        bool showHidden,
        IFileSystemProvider provider,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? columnSets = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(provider);
        var tabs = await OpenTabsAsync(paths, sort, showHidden, provider, cancellationToken, columnSets).ConfigureAwait(false);
        if (tabs.Count == 0)
        {
            tabs.Add(await OpenTabAsync(LocalFileSystem.HomeDirectory(), sort, showHidden, provider, cancellationToken)
                .ConfigureAwait(false));
        }

        return new Panel(tabs, Clamp(active, tabs.Count), provider);
    }

    /// <summary>A panel holding exactly <paramref name="tabs"/> (at least one), tab <paramref name="active"/> current.</summary>
    public static async Task<Panel> OpenStartsAsync(
        IReadOnlyList<FavoriteTabStart> tabs,
        int active,
        bool showHidden,
        IFileSystemProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentOutOfRangeException.ThrowIfZero(tabs.Count);
        var opened = await OpenStartTabsAsync(tabs, showHidden, provider, cancellationToken).ConfigureAwait(false);
        return new Panel(opened, Clamp(active, opened.Count), provider);
    }

    /// <summary>Appends <paramref name="tabs"/> after the open ones; a non-negative <paramref name="active"/> (index in the result) becomes the current tab.</summary>
    public async Task AddStartsAsync(
        IReadOnlyList<FavoriteTabStart> tabs,
        int active,
        bool showHidden,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        _tabs.AddRange(await OpenStartTabsAsync(tabs, showHidden, _provider, cancellationToken).ConfigureAwait(false));
        if (active >= 0)
        {
            ActiveTab = Clamp(active, _tabs.Count);
        }
    }

    public async Task NewTabAsync(bool showHidden, CancellationToken cancellationToken = default)
    {
        var tab = new Tab(Active.Location, Active.Sort, _provider) { ColumnSetName = Active.ColumnSetName };
        await tab.ReloadAsync(showHidden, cancellationToken).ConfigureAwait(false);
        _tabs.Insert(ActiveTab + 1, tab);
        ActiveTab++;
    }

    /// <summary>Opens <paramref name="location"/> (a virtual folder such as search results) in a new tab next to the active one.</summary>
    public async Task NewTabAtAsync(Location location, bool showHidden, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        var tab = new Tab(location, Active.Sort, _provider) { ColumnSetName = Active.ColumnSetName };
        await tab.ReloadAsync(showHidden, cancellationToken).ConfigureAwait(false);
        if (tab.Error is not null)
        {
            return;
        }

        _tabs.Insert(ActiveTab + 1, tab);
        ActiveTab++;
    }

    public void CloseTab()
    {
        if (_tabs.Count <= 1)
        {
            return;
        }

        _tabs.RemoveAt(ActiveTab);
        if (ActiveTab >= _tabs.Count)
        {
            ActiveTab = _tabs.Count - 1;
        }
    }

    public void NextTab() => ActiveTab = (ActiveTab + 1) % _tabs.Count;

    /// <summary>Folder of every tab, for a later session save.</summary>
    public IReadOnlyList<string> TabDirectories()
    {
        var paths = new List<string>(_tabs.Count);
        foreach (var tab in _tabs)
        {
            paths.Add(tab.Location.Path);
        }

        return paths;
    }

    private static async Task<List<Tab>> OpenTabsAsync(
        IReadOnlyList<string> paths,
        SortOrder sort,
        bool showHidden,
        IFileSystemProvider provider,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? columnSets)
    {
        var tabs = new List<Tab>();
        for (var i = 0; i < paths.Count; i++)
        {
            if (!Directory.Exists(paths[i]))
            {
                continue;
            }

            var tab = await OpenTabAsync(paths[i], sort, showHidden, provider, cancellationToken).ConfigureAwait(false);
            if (columnSets is not null && i < columnSets.Count && !string.IsNullOrWhiteSpace(columnSets[i]))
            {
                tab.ColumnSetName = columnSets[i];
            }

            tabs.Add(tab);
        }

        return tabs;
    }

    private static async Task<List<Tab>> OpenStartTabsAsync(
        IReadOnlyList<FavoriteTabStart> starts,
        bool showHidden,
        IFileSystemProvider provider,
        CancellationToken cancellationToken)
    {
        var tabs = new List<Tab>(starts.Count);
        foreach (var start in starts)
        {
            var tab = new Tab(new Location(start.Path), start.Sort, provider) { ColumnSetName = start.ColumnSet };
            await tab.ReloadAsync(showHidden, cancellationToken).ConfigureAwait(false);
            tabs.Add(tab);
        }

        return tabs;
    }

    private static async Task<Tab> OpenTabAsync(
        string path,
        SortOrder sort,
        bool showHidden,
        IFileSystemProvider provider,
        CancellationToken cancellationToken)
    {
        var tab = new Tab(new Location(path), sort, provider);
        await tab.ReloadAsync(showHidden, cancellationToken).ConfigureAwait(false);
        return tab;
    }

    private static int Clamp(int active, int count)
    {
        if (active < 0)
        {
            return 0;
        }

        if (active >= count)
        {
            return count - 1;
        }

        return active;
    }
}
