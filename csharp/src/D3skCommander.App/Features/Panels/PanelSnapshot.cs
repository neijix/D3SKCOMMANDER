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
    string? ErrorPath)
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

    public static PanelSnapshot Capture(Tab tab, FileListView view, int generation, int failureToken)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var entries = new List<Entry>();
        var marked = new List<string>();
        var selectable = 0;
        foreach (var entry in tab.VisibleEntries)
        {
            entries.Add(entry);
            if (entry.IsParent)
            {
                continue;
            }

            selectable++;
            if (tab.IsSelected(entry.Path))
            {
                marked.Add(entry.Path);
            }
        }

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
            error?.Path ?? error?.Message);
    }
}
