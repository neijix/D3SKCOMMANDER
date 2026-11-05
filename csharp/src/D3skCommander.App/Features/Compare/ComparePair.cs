using D3skCommander.App.Features.Panels;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Features.Compare;

/// <summary>The two files a compare opens: two marked files, or one file in each panel.</summary>
public static class ComparePair
{
    public static bool TryChoose(
        PanelSnapshot active,
        PanelSnapshot other,
        bool activeIsLeft,
        out string left,
        out string right)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(other);
        left = "";
        right = "";
        var mine = Files(Targets(active));
        if (mine.Count >= 2)
        {
            left = mine[0].Path;
            right = mine[1].Path;
            return true;
        }

        var theirs = Files(Targets(other));
        if (mine.Count == 0 || theirs.Count == 0)
        {
            return false;
        }

        if (activeIsLeft)
        {
            left = mine[0].Path;
            right = theirs[0].Path;
            return true;
        }

        left = theirs[0].Path;
        right = mine[0].Path;
        return true;
    }

    private static List<Entry> Targets(PanelSnapshot snap)
    {
        var marked = new HashSet<string>(snap.Marked, StringComparer.Ordinal);
        if (marked.Count > 0)
        {
            return Marked(snap.Entries, marked);
        }

        if (snap.Cursor < 0 || snap.Cursor >= snap.Entries.Count)
        {
            return [];
        }

        var current = snap.Entries[snap.Cursor];
        if (current.IsParent)
        {
            return [];
        }

        return [current];
    }

    private static List<Entry> Marked(IReadOnlyList<Entry> entries, HashSet<string> marked)
    {
        var selected = new List<Entry>();
        foreach (var entry in entries)
        {
            if (marked.Contains(entry.Path))
            {
                selected.Add(entry);
            }
        }

        return selected;
    }

    private static List<Entry> Files(List<Entry> entries)
    {
        var files = new List<Entry>();
        foreach (var entry in entries)
        {
            if (!entry.IsDirectory && !entry.IsParent)
            {
                files.Add(entry);
            }
        }

        return files;
    }
}
