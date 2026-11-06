using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Features.Drives;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Tree;

namespace D3skCommander.App.Features.Tree;

/// <summary>
/// The folder tree of one panel: roots (the home folder, then the mounted volumes), children read lazily off the
/// UI thread, and a selection that follows the panel. <see cref="Open"/> (a click or Enter on a node) goes to
/// <c>navigate</c>; changing the selection never does.
/// </summary>
public sealed partial class FolderTreeModel : ObservableObject
{
    private static readonly TimeSpan RootsEvery = TimeSpan.FromSeconds(3);

    private readonly FolderTreeLoader _loader;
    private readonly IDriveSource? _drives;
    private readonly ILocalizer _text;
    private readonly Func<bool> _showHidden;
    private readonly Action<string> _navigate;
    private readonly string _home;
    private readonly CancellationTokenSource _lifetime = new();
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
        string? home = null)
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
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
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

    /// <summary>Reads the roots on a worker (volumes are listed by the OS) and merges them into <see cref="Roots"/>.</summary>
    public async Task LoadRootsAsync()
    {
        _rootsRead = DateTimeOffset.UtcNow;
        var found = await Task.Run(ReadRoots).ConfigureAwait(true);
        var wanted = found.Where(r => r.Path.Length > 0).DistinctBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var root in wanted)
        {
            if (!Roots.Any(r => FolderTree.SamePath(r.Path, root.Path)))
            {
                Roots.Add(NewNode(root.Name, root.Path));
            }
        }

        for (var i = Roots.Count - 1; i >= 0; i--)
        {
            var path = Roots[i].Path;
            if (!wanted.Exists(r => FolderTree.SamePath(r.Path, path)))
            {
                Roots.RemoveAt(i);
            }
        }
    }

    /// <summary>Loads the children of <paramref name="node"/> once. A second call while it loads waits for the same read.</summary>
    public Task LoadAsync(FolderTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Loaded || node.IsPlaceholder)
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

    internal FolderTreeNode NewNode(string name, string path) =>
        new(name, path, FolderNodeKind.Folder, _text.Get("tree.loading"), node => _ = LoadAsync(node));

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

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < node.Children.Count && ReferenceEquals(node.Children[i], wanted[i]))
            {
                continue;
            }

            var at = node.Children.IndexOf(wanted[i]);
            if (at > i)
            {
                node.Children.Move(at, i);
            }
            else
            {
                node.Children.Insert(i, wanted[i]);
            }
        }

        while (node.Children.Count > wanted.Count)
        {
            node.Children.RemoveAt(node.Children.Count - 1);
        }
    }

    private List<(string Name, string Path)> ReadRoots()
    {
        var list = new List<(string, string)>();
        if (_home.Length > 0 && Directory.Exists(_home))
        {
            list.Add((_text.Get("tree.home"), _home));
        }

        try
        {
            foreach (var drive in _drives?.Mounted() ?? [])
            {
                list.Add((DriveList.ShortName(drive.Path), drive.Path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The volumes cannot be listed: the home folder stays.
        }

        return list;
    }
}
