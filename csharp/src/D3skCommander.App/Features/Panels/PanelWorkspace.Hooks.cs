using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Shell;
using D3skCommander.Core.Providers;

namespace D3skCommander.App.Features.Panels;

/// <summary>
/// Hooks other features plug into the panels: the in-place rename (file operations commit the new
/// name) and the file opener Enter uses on a file it cannot enter (the launch feature).
/// </summary>
public sealed partial class PanelWorkspace : IInPlaceRename
{
    /// <summary>Renames the entry at the given path to the given name; set by the file operations.</summary>
    public Func<string, string, Task>? Commit { get; set; }

    /// <summary>Opens a local file with its default application; set by the launch feature.</summary>
    public Action<string>? OpenFile { get; set; }

    /// <summary>Raised with the active side when a rename starts; that side's pane opens its name editor.</summary>
    public event EventHandler<PanelSide>? RenameRequested;

    public void Begin() => RenameRequested?.Invoke(this, _active);

    /// <summary>The pane's name editor committed: the file operations rename the entry.</summary>
    public void CommitRename(string path, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var commit = Commit;
        if (commit is not null)
        {
            Run(() => commit(path, name));
        }
    }

    /// <summary>
    /// The local file under the cursor of a side, which Enter opens with its default application,
    /// or null for a folder, the parent entry, an archive (the panel enters it) or a remote entry.
    /// </summary>
    public string? FileToOpen(PanelSide side)
    {
        var snap = Read(side);
        if (OpenFile is null || snap.Cursor < 0 || snap.Cursor >= snap.Entries.Count)
        {
            return null;
        }

        var entry = snap.Entries[snap.Cursor];
        if (entry.IsDirectory || entry.IsParent || ArchiveFileSystemProvider.IsArchiveFile(entry.Path))
        {
            return null;
        }

        return File.Exists(entry.Path) ? entry.Path : null;
    }
}
