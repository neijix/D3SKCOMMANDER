using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Settings;

namespace D3skCommander.Core.Panels;

/// <summary>What a panel reopens at start: its tab folders, the active tab, the sort and the view.</summary>
public sealed record PanelStart(IReadOnlyList<string> Paths, int Active, SortOrder Sort, PanelViewMode View);

/// <summary>
/// The panel part of the session: a panel's tabs saved on every change, and reopened at start.
/// A saved folder that no longer exists is dropped; no folder left means the home folder.
/// </summary>
public static class PanelSession
{
    public static PanelStart Restore(SessionPanel? saved, string home, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrEmpty(home);
        ArgumentNullException.ThrowIfNull(exists);
        var paths = new List<string>();
        var active = 0;
        SessionTab? activeTab = null;
        if (saved is not null)
        {
            for (var i = 0; i < saved.Tabs.Count; i++)
            {
                var tab = saved.Tabs[i];
                if (string.IsNullOrEmpty(tab.Directory) || !exists(tab.Directory))
                {
                    continue;
                }

                if (i <= saved.ActiveIndex || activeTab is null)
                {
                    active = paths.Count;
                    activeTab = tab;
                }

                paths.Add(tab.Directory);
            }
        }

        if (paths.Count == 0 || activeTab is null)
        {
            return new PanelStart([home], 0, SortOrder.NameAscending, PanelViewMode.Full);
        }

        return new PanelStart(paths, active, activeTab.Sort, activeTab.ViewMode);
    }

    /// <summary>The panel's tabs as the session keeps them; every tab carries the panel's view.</summary>
    public static SessionPanel Capture(Panel panel, PanelViewMode view)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var saved = new SessionPanel { ActiveIndex = panel.ActiveTab };
        foreach (var tab in panel.Tabs)
        {
            saved.Tabs.Add(new SessionTab { Directory = tab.Location.Path, Sort = tab.Sort, ViewMode = view });
        }

        return saved;
    }
}
