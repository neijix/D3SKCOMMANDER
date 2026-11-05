using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.Core.Compare;

/// <summary>How many files <see cref="ComparePanels.Mark"/> selected on each side.</summary>
public readonly record struct ComparePanelCounts(int Left, int Right);

/// <summary>Shift+F2: mark the files that are missing on the other side, or newer by more than two seconds.</summary>
public static class ComparePanels
{
    /// <summary>v0.1's slack, so a two-second filesystem timestamp does not count as newer.</summary>
    public const int NewerSlackSeconds = 2;

    /// <summary>
    /// Clears both selections, then marks each file that the other side lacks or that is older there
    /// by more than <see cref="NewerSlackSeconds"/>. Directories are left unselected.
    /// </summary>
    public static ComparePanelCounts Mark(
        IReadOnlyList<Entry> leftEntries,
        Selection leftSelection,
        IReadOnlyList<Entry> rightEntries,
        Selection rightSelection)
    {
        ArgumentNullException.ThrowIfNull(leftEntries);
        ArgumentNullException.ThrowIfNull(leftSelection);
        ArgumentNullException.ThrowIfNull(rightEntries);
        ArgumentNullException.ThrowIfNull(rightSelection);
        var left = Files(leftEntries);
        var right = Files(rightEntries);
        var leftCount = Select(left, right, leftSelection);
        var rightCount = Select(right, left, rightSelection);
        return new ComparePanelCounts(leftCount, rightCount);
    }

    private static int Select(Dictionary<string, Entry> mine, Dictionary<string, Entry> theirs, Selection selection)
    {
        selection.Clear();
        var count = 0;
        foreach (var entry in mine.Values)
        {
            if (Differs(entry, theirs))
            {
                selection.Add(entry.Path);
                count++;
            }
        }

        return count;
    }

    private static bool Differs(Entry entry, Dictionary<string, Entry> theirs)
    {
        if (!theirs.TryGetValue(entry.Name, out var other))
        {
            return true;
        }

        return IsNewer(entry.Modified, other.Modified);
    }

    /// <summary>v0.1 compares <c>Option&lt;SystemTime&gt;</c>: a missing time is older than any time, and equal times are not newer.</summary>
    private static bool IsNewer(DateTimeOffset? mine, DateTimeOffset? other)
    {
        if (mine is null)
        {
            return false;
        }

        if (other is null)
        {
            return true;
        }

        return mine.Value > other.Value.AddSeconds(NewerSlackSeconds);
    }

    private static Dictionary<string, Entry> Files(IReadOnlyList<Entry> entries)
    {
        var files = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!entry.IsDirectory)
            {
                files[entry.Name] = entry;
            }
        }

        return files;
    }
}
