using D3skCommander.Core.Commands;

namespace D3skCommander.Core.Toolbar;

/// <summary>Rules for a list of toolbar items: cleaning what was read, and the editor's move and remove.</summary>
public static class ToolbarItems
{
    /// <summary>
    /// Keeps the entries that can run: a catalog command that exists, a user command with a name, a program
    /// with a path, or a separator. An unknown icon is cleared; text fields are trimmed.
    /// </summary>
    public static List<ToolbarItem> Clean(IEnumerable<ToolbarItem?>? items)
    {
        var kept = new List<ToolbarItem>();
        foreach (var item in items ?? [])
        {
            if (Clean(item) is { } clean)
            {
                kept.Add(clean);
            }
        }

        return kept;
    }

    /// <summary>The cleaned item, or null when it cannot run or is of an unknown kind.</summary>
    public static ToolbarItem? Clean(ToolbarItem? item)
    {
        if (item is null || !Enum.IsDefined(item.Kind))
        {
            return null;
        }

        var icon = (item.Icon ?? "").Trim();
        icon = ToolbarIcons.IsKnown(icon) ? icon : "";
        var tooltip = (item.Tooltip ?? "").Trim();
        switch (item.Kind)
        {
            case ToolbarItemKind.Separator:
                return ToolbarItem.Separator;
            case ToolbarItemKind.Command:
                return item.Command is CommandId command && Enum.IsDefined(command)
                    ? ToolbarItem.ForCommand(command, icon, tooltip)
                    : null;
            case ToolbarItemKind.UserCommand:
                var name = (item.UserCommandName ?? "").Trim();
                return name.Length == 0 ? null : ToolbarItem.ForUserCommand(name, icon, tooltip);
            default:
                var program = (item.Program ?? "").Trim();
                return program.Length == 0
                    ? null
                    : ToolbarItem.ForProgram(program, (item.Arguments ?? "").Trim(), (item.WorkingFolder ?? "").Trim(), icon, tooltip);
        }
    }

    /// <summary>
    /// Moves the item at <paramref name="index"/> by <paramref name="delta"/> places (negative is up).
    /// Returns its new index, the same index when it cannot move.
    /// </summary>
    public static int Move<T>(IList<T> items, int index, int delta)
    {
        ArgumentNullException.ThrowIfNull(items);
        var to = index + delta;
        if (index < 0 || index >= items.Count || to < 0 || to >= items.Count || delta == 0)
        {
            return index;
        }

        var item = items[index];
        items.RemoveAt(index);
        items.Insert(to, item);
        return to;
    }

    /// <summary>
    /// Removes the item at <paramref name="index"/>. Returns the index to select next (the one that
    /// took its place, else the last), or -1 when the list is empty or the index was not valid.
    /// </summary>
    public static int Remove<T>(IList<T> items, int index)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (index < 0 || index >= items.Count)
        {
            return -1;
        }

        items.RemoveAt(index);
        return items.Count == 0 ? -1 : Math.Min(index, items.Count - 1);
    }
}
