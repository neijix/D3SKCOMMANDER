using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Wildcard;

namespace D3skCommander.Core.Panels;

public sealed partial class Tab
{
    public void ClearSelection() => _selected.Clear();

    /// <summary>The marked rows, or the row under the cursor when nothing is marked. ".." is never a target.</summary>
    public IReadOnlyList<Entry> Targets()
    {
        var selected = new List<Entry>();
        foreach (var entry in VisibleEntries)
        {
            if (_selected.Contains(entry.Path))
            {
                selected.Add(entry);
            }
        }

        if (selected.Count > 0)
        {
            return selected;
        }

        var current = Current;
        if (current is null || current.IsParent)
        {
            return [];
        }

        return [current];
    }

    /// <summary>Shift adds the range from the cursor, Ctrl toggles, a plain press clears. The cursor moves to <paramref name="index"/>.</summary>
    public void Click(int index, bool ctrl, bool shift)
    {
        if (!Visible(index))
        {
            return;
        }

        if (shift)
        {
            AddRange(index);
        }
        else if (ctrl)
        {
            ToggleAt(index);
        }
        else
        {
            _selected.Clear();
        }

        Cursor = index;
    }

    /// <summary>A right press. The selection stays when the row is part of it; otherwise it is dropped.</summary>
    public void Point(int index)
    {
        if (!Visible(index))
        {
            return;
        }

        var entry = EntryAt(_visible[index]);
        if (entry is null || !_selected.Contains(entry.Path))
        {
            _selected.Clear();
        }

        Cursor = index;
    }

    public void SelectMask(string mask, bool select, bool includeDirectories)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var parsed = new Mask(mask);
        foreach (var entry in VisibleEntries)
        {
            if (!MaskHit(entry, parsed, includeDirectories))
            {
                continue;
            }

            if (select)
            {
                _selected.Add(entry.Path);
            }
            else
            {
                _selected.Remove(entry.Path);
            }
        }
    }

    public void SelectAll()
    {
        foreach (var entry in VisibleEntries)
        {
            if (!entry.IsParent)
            {
                _selected.Add(entry.Path);
            }
        }
    }

    public void InvertSelection()
    {
        foreach (var entry in VisibleEntries.Where(entry => !entry.IsParent && !entry.IsDirectory).ToList())
        {
            _selected.Toggle(entry.Path);
        }
    }

    private void AddRange(int index)
    {
        var from = Cursor;
        if (from >= _visible.Count)
        {
            from = _visible.Count - 1;
        }

        var start = Math.Min(from, index);
        var end = Math.Max(from, index);
        for (var i = start; i <= end; i++)
        {
            var entry = EntryAt(_visible[i]);
            if (entry is null || entry.IsParent)
            {
                continue;
            }

            _selected.Add(entry.Path);
        }
    }

    private void ToggleAt(int index)
    {
        var entry = EntryAt(_visible[index]);
        if (entry is null || entry.IsParent)
        {
            return;
        }

        _selected.Toggle(entry.Path);
    }

    private bool Visible(int index) => index >= 0 && index < _visible.Count;

    private static bool MaskHit(Entry entry, Mask mask, bool includeDirectories)
    {
        if (entry.IsParent || (!includeDirectories && entry.IsDirectory))
        {
            return false;
        }

        return mask.Matches(entry.Name);
    }
}
