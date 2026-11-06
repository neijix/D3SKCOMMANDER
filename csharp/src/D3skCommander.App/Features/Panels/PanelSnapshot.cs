using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Panels;

/// <summary>One consistent read of a panel for the frame that draws it.</summary>
public sealed record PanelSnapshot(
    string Path,
    IReadOnlyList<Entry> Entries,
    int Cursor,
    int Selected,
    int Selectable,
    IReadOnlyList<string> Marked,
    FileListView View,
    int Generation,
    int FailureToken,
    ErrorKind? Error,
    string? ErrorPath,
    string? Filter = null,
    long Version = 0,
    ulong SelectedBytes = 0,
    ulong TotalBytes = 0)
{
    public static PanelSnapshot Empty { get; } = new(
        "",
        [],
        0,
        0,
        0,
        [],
        FileListView.Full,
        0,
        0,
        null,
        null);

    /// <summary>
    /// Reads <paramref name="tab"/>. When <paramref name="previous"/> shows the same rows
    /// (<see cref="Tab.Version"/>), its entry list is kept: the same list tells the view not to rebuild.
    /// </summary>
    public static PanelSnapshot Capture(Tab tab, FileListView view, int generation, int failureToken, PanelSnapshot? previous = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var same = previous is not null && previous.Version == tab.Version;
        var entries = same ? previous!.Entries : [.. tab.VisibleEntries];
        var selectable = same ? previous!.Selectable : CountSelectable(entries);
        var marked = new List<string>();
        if (tab.SelectedCount > 0)
        {
            foreach (var entry in entries)
            {
                if (!entry.IsParent && tab.IsSelected(entry.Path))
                {
                    marked.Add(entry.Path);
                }
            }
        }

        var totalBytes = same ? previous!.TotalBytes : PanelTotals.SumBytes(entries);
        var selectedBytes = marked.Count > 0 ? PanelTotals.MarkedBytes(entries, marked) : 0UL;
        var error = tab.Error;
        return new PanelSnapshot(
            tab.Location.Path,
            entries,
            tab.Cursor,
            marked.Count,
            selectable,
            marked,
            view,
            generation,
            failureToken,
            error?.Kind,
            error?.Path ?? error?.Message,
            tab.Filter,
            tab.Version,
            selectedBytes,
            totalBytes);
    }

    private static int CountSelectable(IReadOnlyList<Entry> entries)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            if (!entry.IsParent)
            {
                count++;
            }
        }

        return count;
    }
}
