namespace D3skCommander.Core.FileSystem;

/// <summary>One row of a panel. A link to a folder is both a link and a directory.</summary>
public sealed class Entry
{
    private string _name;
    private string? _stem;
    private string? _extension;

    public Entry(
        string name,
        string path,
        bool isDirectory,
        bool isLink,
        bool isParent,
        ulong size,
        DateTimeOffset? modified,
        bool hidden,
        bool readOnly,
        string attributes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(attributes);
        _name = name;
        Path = path;
        IsDirectory = isDirectory;
        IsLink = isLink;
        IsParent = isParent;
        Size = size;
        Modified = modified;
        Hidden = hidden;
        ReadOnly = readOnly;
        Attributes = attributes;
    }

    /// <summary>Display name. In a branch listing this is the path relative to the listed folder.</summary>
    public string Name
    {
        get => _name;
        internal set
        {
            _name = value;
            _stem = null;
            _extension = null;
        }
    }

    /// <summary>
    /// Group number (1-based) of a row in a duplicates listing; 0 outside one. Rows of a group stay together
    /// whatever the sort column. The Computer view numbers its rows too: drives 1, network drives 2,
    /// places 3, favorites 4 (display groups, not duplicates: select-all-but-first ignores them there).
    /// </summary>
    public int Group { get; internal set; }

    public string Path { get; }

    public bool IsDirectory { get; }

    public bool IsLink { get; }

    /// <summary>The ".." row.</summary>
    public bool IsParent { get; }

    public ulong Size { get; }

    public DateTimeOffset? Modified { get; }

    public bool Hidden { get; }

    public bool ReadOnly { get; }

    /// <summary>"rwxr-xr-x" on Unix, "rahs" on Windows.</summary>
    public string Attributes { get; }

    /// <summary>Extension of a file; empty for folders, dot-files and names without a dot.</summary>
    /// <remarks>Kept after the first read: sorting by extension asks for it on every comparison.</remarks>
    public string Extension => IsDirectory ? "" : _extension ??= LocalFileSystem.SplitExtension(_name).Extension;

    /// <summary>Name without its extension. Folders keep their full name.</summary>
    public string Stem => IsDirectory ? _name : _stem ??= LocalFileSystem.SplitExtension(_name).Stem;
}
