namespace D3skCommander.Core.Tree;

/// <summary>One folder under a tree node: its display name and full path.</summary>
public sealed record FolderChild(string Name, string Path);

/// <summary>The folders under one node. <see cref="Failed"/> is set when the folder could not be read; <see cref="Folders"/> is then empty.</summary>
public sealed record FolderChildren(IReadOnlyList<FolderChild> Folders, bool Failed)
{
    public static FolderChildren Empty { get; } = new([], false);

    public static FolderChildren Failure { get; } = new([], true);
}
