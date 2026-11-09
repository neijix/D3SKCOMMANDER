namespace D3skCommander.Core.DiskUsage;

/// <summary>Edits of a scanned tree after the fact: the nodes are immutable, so an edit rebuilds the ancestors.</summary>
public static class SizeTree
{
    /// <summary>
    /// The tree under <paramref name="root"/> without <paramref name="node"/> (deleted on disk), every ancestor's totals
    /// updated. Returns the new root and the rebuilt parent of the removed node, the folder to show next.
    /// </summary>
    public static (SizeNode Root, SizeNode Parent) Remove(SizeNode root, SizeNode node)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(node);
        var parent = node.Parent ?? throw new ArgumentException("The scanned folder itself cannot be removed.", nameof(node));
        var rebuilt = SizeNode.Folder(parent.Name, parent.Path, parent.Children.Where(child => !ReferenceEquals(child, node)).ToList(), parent.IsUnreadable);
        var shown = rebuilt;
        var old = parent;
        while (!ReferenceEquals(old, root))
        {
            var up = old.Parent ?? throw new ArgumentException("The node is not under the root.", nameof(node));
            var replaced = old;
            var child = rebuilt;
            rebuilt = SizeNode.Folder(up.Name, up.Path, up.Children.Select(c => ReferenceEquals(c, replaced) ? child : c).ToList(), up.IsUnreadable);
            old = up;
        }

        return (rebuilt, shown);
    }
}
