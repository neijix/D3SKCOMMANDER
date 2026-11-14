namespace D3skCommander.Core.FileSystem;

/// <summary>Where a listing of grouped rows (<see cref="Entry.Group"/>) draws a line: above the first row of each group but the first.</summary>
public static class EntryGroups
{
    /// <summary>Whether <paramref name="entry"/> opens another group than the row above it (none above: no line).</summary>
    public static bool StartsGroup(Entry? previous, Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return previous is { Group: > 0, IsParent: false } && entry is { Group: > 0, IsParent: false } && previous.Group != entry.Group;
    }
}
