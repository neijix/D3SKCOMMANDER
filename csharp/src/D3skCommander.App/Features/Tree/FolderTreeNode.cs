using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace D3skCommander.App.Features.Tree;

/// <summary>What a tree row stands for: a real folder, or a dim placeholder under a node.</summary>
public enum FolderNodeKind
{
    Folder,
    Loading,
    NoAccess,
}

/// <summary>
/// One row of the folder tree. A folder starts with a "…" placeholder child, so its expander shows
/// before anything is read; the children are loaded when the node expands.
/// </summary>
public sealed partial class FolderTreeNode : ObservableObject
{
    private readonly Action<FolderTreeNode>? _expanded;
    [ObservableProperty]
    private bool _isExpanded;

    public FolderTreeNode(string name, string path, FolderNodeKind kind = FolderNodeKind.Folder, string? loadingText = null, Action<FolderTreeNode>? expanded = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(path);
        Name = name;
        Path = path;
        Kind = kind;
        _expanded = expanded;
        if (kind == FolderNodeKind.Folder)
        {
            Children.Add(new FolderTreeNode(loadingText ?? "…", path, FolderNodeKind.Loading));
        }
    }

    public string Name { get; }

    public string Path { get; }

    public FolderNodeKind Kind { get; }

    public bool IsPlaceholder => Kind != FolderNodeKind.Folder;

    public ObservableCollection<FolderTreeNode> Children { get; } = [];

    /// <summary>The children have been read (or failed to be); until then the only child is the placeholder.</summary>
    public bool Loaded { get; internal set; }

    internal Task? Loading { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            _expanded?.Invoke(this);
        }
    }
}
