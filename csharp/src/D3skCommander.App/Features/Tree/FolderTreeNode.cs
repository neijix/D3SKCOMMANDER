using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.Core.FileKinds;

namespace D3skCommander.App.Features.Tree;

/// <summary>What a tree row stands for: a real folder, a place opened without children, a line, or a dim placeholder.</summary>
public enum FolderNodeKind
{
    Folder,
    Loading,
    NoAccess,

    /// <summary>A root the panel opens but the tree does not expand: Home (<c>computer:</c>), a network server.</summary>
    Place,

    /// <summary>The line between the drives and the favorites.</summary>
    Separator,
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

    /// <summary>The icon before the name: a folder, or a drive / network drive glyph on a root.</summary>
    public FileKind Icon { get; init; } = FileKind.Folder;

    /// <summary>A root that comes from the favorites (under the line), not from the drives.</summary>
    public bool IsFavorite { get; init; }

    /// <summary>Nowhere to go: the loading and no-access rows, and the line.</summary>
    public bool IsPlaceholder => Kind is FolderNodeKind.Loading or FolderNodeKind.NoAccess or FolderNodeKind.Separator;

    public bool IsSeparator => Kind == FolderNodeKind.Separator;

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
