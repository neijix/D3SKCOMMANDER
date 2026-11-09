using Avalonia;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Shell;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;

namespace D3skCommander.App.Features.Panels;

/// <summary>
/// Hooks other features plug into the panels: the in-place rename (file operations commit the new
/// name) and the file opener Enter uses on a file it cannot enter (the launch feature).
/// </summary>
public sealed partial class PanelWorkspace : IInPlaceRename
{
    private readonly Func<PixelPoint?>?[] _rowPoints = new Func<PixelPoint?>?[2];
    private PointHolder? _contextPoint;

    /// <summary>Renames the entry at the given path to the given name; set by the file operations.</summary>
    public Func<string, string, Task>? Commit { get; set; }

    /// <summary>Opens a local file with its default application; set by the launch feature.</summary>
    public Action<string>? OpenFile { get; set; }

    /// <summary>Whether an association opens the file, even one the panel would enter (an archive); set by the launch feature.</summary>
    public Func<string, bool>? HasOpenAssociation { get; set; }

    /// <summary>The context-menu entries for a local file (the associations' programs); set by the launch feature.</summary>
    public Func<string, IReadOnlyList<ShellAction>>? FileMenu { get; set; }

    /// <summary>Whether the file clipboard holds files (Paste is offered only then); unset (no file operations) counts as yes.</summary>
    public Func<bool>? ClipboardHasFiles { get; set; }

    /// <summary>Raised with the active side when <see cref="Core.Commands.CommandId.ContextMenu"/> runs; that side's pane opens the menu for its cursor row.</summary>
    public event EventHandler<PanelSide>? ContextMenuRequested;

    /// <summary>The screen point of the context menu being dispatched (the click, or the cursor row's bottom-left); null outside one.</summary>
    public PixelPoint? ContextScreenPoint => Volatile.Read(ref _contextPoint)?.Point;

    /// <summary>Where a menu-bar or key command opens a native menu: the context menu's point, else the cursor row's bottom-left, else null (the mouse).</summary>
    public PixelPoint? MenuScreenPoint => ContextScreenPoint ?? _rowPoints[(int)_active]?.Invoke();

    /// <summary>A pane tells where its cursor row is on screen (bottom-left), for a command that opens a menu there.</summary>
    public void SetCursorRowPoint(PanelSide side, Func<PixelPoint?>? point) => _rowPoints[(int)side] = point;

    /// <summary>Runs one synchronous dispatch with <paramref name="target"/> as the context target and <paramref name="point"/> as its screen point.</summary>
    public void RunInContext(PanelContextTarget target, PixelPoint? point, Action dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        using var scope = UseContextTarget(target);
        var held = new PointHolder(point);
        Volatile.Write(ref _contextPoint, held);
        try
        {
            dispatch();
        }
        finally
        {
            Interlocked.CompareExchange(ref _contextPoint, null, held);
        }
    }

    /// <summary>
    /// The target of a context menu on the cursor entry of a side, or on empty space (also for the <c>..</c> row and a cursor on no
    /// entry). The cursor must already be on the clicked row. Null before the panel has a folder.
    /// </summary>
    public PanelContextTarget? ContextTargetFor(PanelSide side, bool emptySpace)
    {
        var snap = Read(side);
        if (snap.Path.Length == 0)
        {
            return null;
        }

        var provider = ProviderKindOf(side);
        var entry = snap.Cursor >= 0 && snap.Cursor < snap.Entries.Count ? snap.Entries[snap.Cursor] : null;
        if (emptySpace || entry is null || entry.IsParent)
        {
            // The ".." row carries its own path: at a drive or removable mount it leads to Computer, not to the path's parent.
            var own = !emptySpace && entry is { IsParent: true } ? entry.Path : null;
            return new PanelContextTarget(ContextTargetKind.EmptySpace, provider, snap.Path, own, snap.Marked);
        }

        var kind = ContextTargetKind.File;
        if (entry.IsDirectory)
        {
            kind = ContextTargetKind.Folder;
        }
        else if (provider is PanelProviderKind.Local or PanelProviderKind.FlatList && ArchiveFileSystemProvider.IsArchiveFile(entry.Path))
        {
            kind = ContextTargetKind.ArchiveFile;
        }

        return new PanelContextTarget(kind, provider, snap.Path, entry.Path, snap.Marked);
    }

    private PanelProviderKind ProviderKindOf(PanelSide side)
    {
        if (TabOf(side) is not { } tab)
        {
            return PanelProviderKind.Local;
        }

        var location = tab.Location;
        if (tab.Branch || location.Provider is ListFileSystemProvider)
        {
            return PanelProviderKind.FlatList;
        }

        // Computer is read-only and not a folder: the Remote rules (no favorites, terminals or Windows menu) fit it.
        if (location.Provider is not null || ComputerAddress.IsAddress(location.Path))
        {
            return PanelProviderKind.Remote;
        }

        return ArchiveFileSystemProvider.IsArchivePath(location.Path) ? PanelProviderKind.Archive : PanelProviderKind.Local;
    }

    private sealed record PointHolder(PixelPoint? Point);

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
