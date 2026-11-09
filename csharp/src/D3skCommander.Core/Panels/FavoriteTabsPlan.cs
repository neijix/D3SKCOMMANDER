using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;

namespace D3skCommander.Core.Panels;

/// <summary>One tab to open: a local folder, its sort and its column set.</summary>
public sealed record FavoriteTabStart(string Path, SortOrder Sort, string ColumnSet);

/// <summary>
/// What a panel does for a restore: <see cref="Open"/> the tabs after the ones it keeps
/// (<see cref="KeepOpen"/>) or instead of them, then make tab <see cref="Active"/> of the result current
/// (-1: leave the active tab) and show <see cref="View"/> (null: leave the view).
/// </summary>
public sealed record FavoriteSidePlan(bool KeepOpen, IReadOnlyList<FavoriteTabStart> Open, int Active, PanelViewMode? View);

/// <summary>The plan for both panels and the side to activate.</summary>
public sealed record FavoriteRestorePlan(FavoriteSidePlan Left, FavoriteSidePlan Right, FavoriteSide ActiveSide);

/// <summary>Capture of the open tabs into a <see cref="FavoriteTabSet"/>, and the plan to restore one.</summary>
public static class FavoriteTabsPlan
{
    /// <summary>The tabs of both panels as a set called <paramref name="name"/>; only local folders are kept.</summary>
    public static FavoriteTabSet Capture(
        string name,
        Panel left,
        Panel right,
        PanelViewMode leftView,
        PanelViewMode rightView,
        FavoriteSide active)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new FavoriteTabSet
        {
            Name = name,
            ActiveSide = active,
            Left = CaptureSide(left.Tabs, left.ActiveTab, leftView),
            Right = CaptureSide(right.Tabs, right.ActiveTab, rightView),
        };
    }

    /// <summary>
    /// The local tabs of one panel. When the active tab is not local, the nearest local tab before it
    /// (else the first one) stays active.
    /// </summary>
    public static SessionPanel CaptureSide(IReadOnlyList<Tab> tabs, int active, PanelViewMode view)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var saved = new SessionPanel();
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            if (!IsLocal(tab.Location))
            {
                continue;
            }

            if (i <= active || saved.Tabs.Count == 0)
            {
                saved.ActiveIndex = saved.Tabs.Count;
            }

            saved.Tabs.Add(new SessionTab
            {
                Directory = tab.Location.Path,
                Sort = tab.Sort,
                ViewMode = view,
                ColumnSet = tab.ColumnSetName,
            });
        }

        return saved;
    }

    /// <summary>A folder of this machine: not search results, not inside an archive, not a remote address.</summary>
    public static bool IsLocal(Location location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.Provider is not null || location.Path.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        return !ArchiveFileSystemProvider.IsArchivePath(location.Path);
    }

    /// <summary>
    /// What to open for <paramref name="set"/>. A folder that no longer exists (<paramref name="exists"/>) is skipped.
    /// <see cref="FavoriteRestoreMode.Replace"/> opens <paramref name="home"/> for a side left without a folder;
    /// <see cref="FavoriteRestoreMode.Add"/> keeps the <paramref name="leftOpen"/> / <paramref name="rightOpen"/> open tabs
    /// and makes the set's active tab current only when the side got a tab.
    /// </summary>
    public static FavoriteRestorePlan Restore(
        FavoriteTabSet set,
        FavoriteRestoreMode mode,
        int leftOpen,
        int rightOpen,
        Func<string, bool> exists,
        string home)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentException.ThrowIfNullOrEmpty(home);
        return new FavoriteRestorePlan(
            Side(set.Left, mode, leftOpen, exists, home),
            Side(set.Right, mode, rightOpen, exists, home),
            set.ActiveSide);
    }

    private static FavoriteSidePlan Side(SessionPanel saved, FavoriteRestoreMode mode, int open, Func<string, bool> exists, string home)
    {
        var starts = new List<FavoriteTabStart>();
        var active = 0;
        SessionTab? activeTab = null;
        for (var i = 0; i < saved.Tabs.Count; i++)
        {
            var tab = saved.Tabs[i];
            if (string.IsNullOrEmpty(tab.Directory) || !(ComputerAddress.IsAddress(tab.Directory) || exists(tab.Directory)))
            {
                continue;
            }

            if (i <= saved.ActiveIndex || activeTab is null)
            {
                active = starts.Count;
                activeTab = tab;
            }

            starts.Add(new FavoriteTabStart(tab.Directory, tab.Sort, tab.ColumnSet));
        }

        var keep = mode == FavoriteRestoreMode.Add;
        if (activeTab is null)
        {
            return keep
                ? new FavoriteSidePlan(true, [], -1, null)
                : new FavoriteSidePlan(false, [new FavoriteTabStart(home, SortOrder.NameAscending, ColumnSetCatalog.DefaultName)], 0, null);
        }

        return new FavoriteSidePlan(keep, starts, keep ? open + active : active, activeTab.ViewMode);
    }
}
