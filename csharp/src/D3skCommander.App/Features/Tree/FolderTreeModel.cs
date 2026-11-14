using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Features.Drives;
using D3skCommander.Core.FileKinds;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Tree;

namespace D3skCommander.App.Features.Tree;

/// <summary>
/// The folder tree of one panel: roots (Home, the mounted volumes, then a line and the favorites), children read
/// lazily off the UI thread, and a selection that follows the panel. <see cref="Open"/> (a click or Enter on a node)
/// goes to <c>navigate</c>; changing the selection never does.
/// </summary>
public sealed partial class FolderTreeModel : ObservableObject
{
    private static readonly TimeSpan RootsEvery = TimeSpan.FromSeconds(3);

    private readonly FolderTreeLoader _loader;
    private readonly IDriveSource? _drives;
    private readonly ILocalizer _text;
    private readonly Func<bool> _showHidden;
    private readonly Action<string> _navigate;
    private readonly Func<IReadOnlyList<HotlistItem>> _favorites;
    private readonly CancellationTokenSource _lifetime = new();
    private List<(string Name, string Path)> _favoritesSeen = [];
    private bool _hiddenSeen;
    private int _followId;
    private DateTimeOffset _rootsRead = DateTimeOffset.MinValue;
    [ObservableProperty]
    private FolderTreeNode? _selected;

    public FolderTreeModel(
        FolderTreeLoader loader,
        IDriveSource? drives,
        ILocalizer text,
        Func<bool> showHidden,
        Action<string> navigate,
        Func<IReadOnlyList<HotlistItem>>? favorites = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(showHidden);
        ArgumentNullException.ThrowIfNull(navigate);
        _loader = loader;
        _drives = drives;
        _text = text;
        _showHidden = showHidden;
        _navigate = navigate;
        _favorites = favorites ?? (() => []);
        _hiddenSeen = showHidden();
    }

    public ObservableCollection<FolderTreeNode> Roots { get; } = [];

    public string OpenOtherText => _text.Get("tree.open_other");

    public string RefreshText => _text.Get("tree.refresh");

    /// <summary>A node was selected by the follow and should be scrolled into view.</summary>
    public event EventHandler<FolderTreeNode>? ScrollRequested;

    /// <summary>The user went to the node (selection or Enter): the panel goes to its folder.</summary>
    public void Open(FolderTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!node.IsPlaceholder)
        {
            _navigate(node.Path);
        }
    }

    /// <summary>
    /// Reads the roots on a worker (volumes are listed by the OS, favorites are checked on disk) and merges them into
    /// <see cref="Roots"/>: Home, the drives, then a line and the favorites. A root still there keeps its node.
    /// </summary>
    public async Task LoadRootsAsync()
    {
        _rootsRead = DateTimeOffset.UtcNow;
        var favorites = Snapshot(_favorites());
        _favoritesSeen = favorites;
        var found = await Task.Run(() => ReadRoots(favorites)).ConfigureAwait(true);
        var wanted = new List<FolderTreeNode>(found.Count);
        foreach (var root in found)
        {
            var kept = Roots.FirstOrDefault(r => !wanted.Contains(r) && r.Kind == root.Kind && r.IsFavorite == root.Favorite
                && r.Name == root.Name && (root.Kind == FolderNodeKind.Separator || FolderTree.SamePath(r.Path, root.Path)));
            wanted.Add(kept ?? RootNode(root));
        }

        Reorder(Roots, wanted);
    }

    /// <summary>The favorites changed since the roots were read (added in Settings, say): the tree reads them again.</summary>
    public bool FavoritesChanged() => !Snapshot(_favorites()).SequenceEqual(_favoritesSeen);

    // The items are mutable (Settings edits them in place): the tree keeps a copy of what it showed.
    private static List<(string Name, string Path)> Snapshot(IReadOnlyList<HotlistItem> items) =>
        [.. items.Select(f => (f.Name, f.Path))];

    /// <summary>Loads the children of <paramref name="node"/> once. A second call while it loads waits for the same read.</summary>
    public Task LoadAsync(FolderTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Loaded || node.Kind != FolderNodeKind.Folder)
        {
            return Task.CompletedTask;
        }

        return node.Loading ??= LoadCoreAsync(node);
    }

    /// <summary>Reads the children of <paramref name="node"/> again, keeping the nodes that are still there.</summary>
    public async Task RefreshNodeAsync(FolderTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Loading is { } running)
        {
            await running.ConfigureAwait(true);
        }

        node.Loaded = false;
        await LoadAsync(node).ConfigureAwait(true);
    }

    internal FolderTreeNode NewNode(string name, string path, FileKind icon = FileKind.Folder) =>
        new(name, path, FolderNodeKind.Folder, _text.Get("tree.loading"), node => _ = LoadAsync(node)) { Icon = icon };

    private async Task LoadCoreAsync(FolderTreeNode node)
    {
        try
        {
            var result = await Task.Run(() => _loader.LoadChildrenAsync(node.Path, _showHidden(), _lifetime.Token)).ConfigureAwait(true);
            Merge(node, result);
            node.Loaded = true;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            node.Loading = null;
        }
    }

    private void Merge(FolderTreeNode node, FolderChildren result)
    {
        var wanted = new List<FolderTreeNode>(result.Folders.Count);
        foreach (var folder in result.Folders)
        {
            var kept = node.Children.FirstOrDefault(c => !c.IsPlaceholder && FolderTree.SamePath(c.Path, folder.Path));
            wanted.Add(kept ?? NewNode(folder.Name, folder.Path));
        }

        if (result.Failed)
        {
            wanted.Add(new FolderTreeNode(_text.Get("tree.no_access"), node.Path, FolderNodeKind.NoAccess));
        }

        Reorder(node.Children, wanted);
    }

    // Makes the list hold exactly the wanted nodes in order, moving the kept ones (an open node stays open).
    private static void Reorder(ObservableCollection<FolderTreeNode> list, List<FolderTreeNode> wanted)
    {
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < list.Count && ReferenceEquals(list[i], wanted[i]))
            {
                continue;
            }

            var at = list.IndexOf(wanted[i]);
            if (at > i)
            {
                list.Move(at, i);
            }
            else
            {
                list.Insert(i, wanted[i]);
            }
        }

        while (list.Count > wanted.Count)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    private FolderTreeNode RootNode(TreeRoot root) => root.Kind == FolderNodeKind.Folder
        ? new FolderTreeNode(root.Name, root.Path, FolderNodeKind.Folder, _text.Get("tree.loading"), node => _ = LoadAsync(node))
        {
            Icon = root.Icon,
            IsFavorite = root.Favorite,
        }
        : new FolderTreeNode(root.Name, root.Path, root.Kind) { Icon = root.Icon, IsFavorite = root.Favorite };

    private List<TreeRoot> ReadRoots(List<(string Name, string Path)> favorites)
    {
        var list = new List<TreeRoot> { new(_text.Get("computer.title"), ComputerAddress.Text, FileKind.Home, FolderNodeKind.Place, false) };
        try
        {
            foreach (var drive in (_drives?.Mounted() ?? []).DistinctBy(d => d.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (drive.Path.Length > 0)
                {
                    list.Add(new(DriveList.ShortName(drive.Path), drive.Path, drive.Network ? FileKind.NetworkDrive : FileKind.Drive, FolderNodeKind.Folder, false));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The volumes cannot be listed: Home and the favorites stay.
        }

        var places = favorites.Where(f => !ComputerAddress.IsAddress(f.Path) && FolderExists(f.Path)).ToList();
        if (places.Count > 0)
        {
            list.Add(new("", "", FileKind.Folder, FolderNodeKind.Separator, false));
        }

        foreach (var favorite in places)
        {
            var name = favorite.Name.Length == 0 ? favorite.Path : favorite.Name;
            var server = UncPath.Server(favorite.Path) is not null;
            var share = UncPath.ShareServer(favorite.Path) is not null;
            list.Add(new(name, favorite.Path, server || share ? FileKind.NetworkDrive : FileKind.Folder, server ? FolderNodeKind.Place : FolderNodeKind.Folder, true));
        }

        return list;
    }

    private static bool FolderExists(string path)
    {
        try
        {
            return path.Length > 0 && UncPath.FolderExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record TreeRoot(string Name, string Path, FileKind Icon, FolderNodeKind Kind, bool Favorite);
}
