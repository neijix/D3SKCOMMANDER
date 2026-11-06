namespace D3skCommander.Core.DiskUsage;

/// <summary>What a <see cref="SizeNode"/> stands for.</summary>
public enum SizeNodeKind
{
    Folder,

    /// <summary>One file, kept because it is among the biggest of its folder (or a link, counted as the link itself).</summary>
    File,

    /// <summary>The files of one folder that are not kept one by one, summed. <see cref="SizeNode.Files"/> says how many.</summary>
    SmallFiles,
}

/// <summary>
/// One node of a scanned size tree: a folder with its totals, or one of the biggest files of a folder.
/// Children are sorted by size, biggest first. Build trees with <see cref="Folder"/>, <see cref="File"/> and
/// <see cref="SmallFiles"/> so the totals always add up.
/// </summary>
public sealed class SizeNode
{
    private static readonly SizeNode[] NoChildren = [];

    private SizeNode(string name, string path, SizeNodeKind kind, IReadOnlyList<SizeNode> children)
    {
        Name = name;
        Path = path;
        Kind = kind;
        Children = children;
    }

    public string Name { get; }

    /// <summary>The folder or file path. A <see cref="SizeNodeKind.SmallFiles"/> node carries its folder's path.</summary>
    public string Path { get; }

    public SizeNodeKind Kind { get; }

    public bool IsFile => Kind == SizeNodeKind.File;

    /// <summary>Everything below (and including, for a file) this node.</summary>
    public long Bytes { get; private init; }

    /// <summary>Files below this node: a file is one, a "smaller files" node is its count.</summary>
    public long Files { get; private init; }

    /// <summary>Folders below this node, not counting itself.</summary>
    public long Folders { get; private init; }

    /// <summary>Folders at or below this node that could not be read.</summary>
    public int Unreadable { get; private init; }

    /// <summary>This very folder could not be read, so its size is a lower bound.</summary>
    public bool IsUnreadable { get; private init; }

    public SizeNode? Parent { get; private set; }

    public IReadOnlyList<SizeNode> Children { get; }

    public static SizeNode File(string name, string path, long bytes) =>
        new(name, path, SizeNodeKind.File, NoChildren) { Bytes = Math.Max(0, bytes), Files = 1 };

    public static SizeNode SmallFiles(string folderPath, long count, long bytes) =>
        new("", folderPath, SizeNodeKind.SmallFiles, NoChildren) { Bytes = Math.Max(0, bytes), Files = Math.Max(0, count) };

    /// <summary>A folder whose totals are the sum of <paramref name="children"/>. They are sorted here, biggest first.</summary>
    public static SizeNode Folder(string name, string path, IEnumerable<SizeNode> children, bool unreadable = false)
    {
        ArgumentNullException.ThrowIfNull(children);
        var sorted = children
            .OrderByDescending(child => child.Bytes)
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        long bytes = 0;
        long files = 0;
        long folders = 0;
        var bad = unreadable ? 1 : 0;
        foreach (var child in sorted)
        {
            bytes += child.Bytes;
            files += child.Files;
            bad += child.Unreadable;
            if (child.Kind == SizeNodeKind.Folder)
            {
                folders += 1 + child.Folders;
            }
        }

        var node = new SizeNode(name, path, SizeNodeKind.Folder, sorted)
        {
            Bytes = bytes,
            Files = files,
            Folders = folders,
            Unreadable = bad,
            IsUnreadable = unreadable,
        };
        foreach (var child in sorted)
        {
            child.Parent = node;
        }

        return node;
    }
}
