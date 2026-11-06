using D3skCommander.Core.Tree;

namespace D3skCommander.App.Features.Tree;

public sealed partial class FolderTreeModel
{
    /// <summary>
    /// Expands the chain from the root that holds <paramref name="path"/> down to it and selects it without navigating.
    /// No root holds the path (an archive, a site, a results list): the selection is cleared.
    /// </summary>
    public async Task FollowAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var id = ++_followId;
        try
        {
            await EnsureRootsAsync(path).ConfigureAwait(true);
            if (id != _followId)
            {
                return;
            }

            var root = FolderTree.RootFor(path, Roots.Select(r => r.Path));
            var node = root is null ? null : Roots.First(r => FolderTree.SamePath(r.Path, root));
            if (root is null || node is null)
            {
                Select(null);
                return;
            }

            var chain = FolderTree.ChainFrom(root, path);
            for (var i = 1; i < chain.Count; i++)
            {
                await LoadAsync(node).ConfigureAwait(true);
                if (id != _followId)
                {
                    return;
                }

                node.IsExpanded = true;
                var step = chain[i];
                var next = node.Children.FirstOrDefault(c => !c.IsPlaceholder && FolderTree.SamePath(c.Path, step));
                if (next is null)
                {
                    break;
                }

                node = next;
            }

            Select(node);
            ScrollRequested?.Invoke(this, node);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>The folder of the panel was listed again: its node, when it was read before, is read again.</summary>
    public async Task RefreshAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (await ForgetHiddenChangeAsync().ConfigureAwait(true))
        {
            return;
        }

        var root = FolderTree.RootFor(path, Roots.Select(r => r.Path));
        var node = root is null ? null : Roots.FirstOrDefault(r => FolderTree.SamePath(r.Path, root));
        if (root is null || node is null)
        {
            return;
        }

        foreach (var step in FolderTree.ChainFrom(root, path).Skip(1))
        {
            node = node.Loaded ? node.Children.FirstOrDefault(c => !c.IsPlaceholder && FolderTree.SamePath(c.Path, step)) : null;
            if (node is null)
            {
                return;
            }
        }

        if (node.Loaded)
        {
            await RefreshNodeAsync(node).ConfigureAwait(true);
        }
    }

    /// <summary>The node of <paramref name="path"/> when it is already shown (its parents are read), else null.</summary>
    public FolderTreeNode? Find(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var root = FolderTree.RootFor(path, Roots.Select(r => r.Path));
        var node = root is null ? null : Roots.FirstOrDefault(r => FolderTree.SamePath(r.Path, root));
        if (root is null)
        {
            return null;
        }

        foreach (var step in FolderTree.ChainFrom(root, path).Skip(1))
        {
            node = node is null ? null : node.Children.FirstOrDefault(c => !c.IsPlaceholder && FolderTree.SamePath(c.Path, step));
        }

        return node;
    }

    // Selecting never navigates (arrow keys only move the selection), so following the panel cannot loop.
    private void Select(FolderTreeNode? node) => Selected = node;

    private async Task EnsureRootsAsync(string path)
    {
        var known = Roots.Count > 0;
        var matched = known && FolderTree.RootFor(path, Roots.Select(r => r.Path)) is not null;
        if (!known || (!matched && DateTimeOffset.UtcNow - _rootsRead > RootsEvery))
        {
            await LoadRootsAsync().ConfigureAwait(true);
        }
    }

    // "Show hidden files" changed since the last read: every node read before is read again.
    private async Task<bool> ForgetHiddenChangeAsync()
    {
        var now = _showHidden();
        if (now == _hiddenSeen)
        {
            return false;
        }

        _hiddenSeen = now;
        foreach (var root in Roots.ToList())
        {
            await ReloadLoadedAsync(root).ConfigureAwait(true);
        }

        return true;
    }

    private async Task ReloadLoadedAsync(FolderTreeNode node)
    {
        if (!node.Loaded)
        {
            return;
        }

        await RefreshNodeAsync(node).ConfigureAwait(true);
        foreach (var child in node.Children.ToList())
        {
            await ReloadLoadedAsync(child).ConfigureAwait(true);
        }
    }
}
