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

    /// <summary>Whether an association opens the file, even one the panel would enter (an archive); set by the launch feature.</summary>
    public Func<string, bool>? HasOpenAssociation { get; set; }

    /// <summary>The context-menu entries for a local file (the associations' programs); set by the launch feature.</summary>
    public Func<string, IReadOnlyList<ShellAction>>? FileMenu { get; set; }

    /// <summary>Raised with the active side when a rename starts; that side's pane opens its name editor.</summary>
    public event EventHandler<PanelSide>? RenameRequested;

    /// <summary>Raised when a pane asks for the size of a folder (Space on it); the panel commands measure it as a job.</summary>
    public event EventHandler<FolderSizeRequest>? FolderSizeRequested;

    public void RequestFolderSize(PanelSide side, string directory, string folder) =>
        FolderSizeRequested?.Invoke(this, new FolderSizeRequest(side, directory, folder));

    public void Begin() => RenameRequested?.Invoke(this, _active);

    /// <summary>The pane's name editor committed: the file operations rename the entry.</summary>
    public void CommitRename(string path, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(name);
        // A search tab names its rows by relative path; an edit that keeps it must not become a move.
        name = Path.GetFileName(name);
        if (name.Length == 0)
        {
            return;
        }

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
        if (entry.IsDirectory || entry.IsParent)
        {
            return null;
        }

        if (ArchiveFileSystemProvider.IsArchiveFile(entry.Path) && HasOpenAssociation?.Invoke(entry.Path) != true)
        {
            return null;
        }

        return File.Exists(entry.Path) ? entry.Path : null;
    }

    /// <summary>The context-menu entries for the local file under the cursor of a side; empty for a folder, the parent entry or a remote entry.</summary>
    public IReadOnlyList<ShellAction> FileMenuFor(PanelSide side)
    {
        var menu = FileMenu;
        var snap = Read(side);
        if (menu is null || snap.Cursor < 0 || snap.Cursor >= snap.Entries.Count)
        {
            return [];
        }

        var entry = snap.Entries[snap.Cursor];
        return entry.IsDirectory || entry.IsParent || !File.Exists(entry.Path) ? [] : menu(entry.Path);
    }
}

/// <summary>A folder whose size a pane wants, listed in <paramref name="Directory"/> on <paramref name="Side"/>.</summary>
public sealed record FolderSizeRequest(PanelSide Side, string Directory, string Folder);
