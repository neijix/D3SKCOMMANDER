namespace D3skCommander.Core.FileSystem;

/// <summary>One row of a panel. A link to a folder is both a link and a directory.</summary>
public sealed class Entry
{
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
        Name = name;
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
    public string Name { get; internal set; }

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
    public string Extension => IsDirectory ? "" : LocalFileSystem.SplitExtension(Name).Extension;

    /// <summary>Name without its extension. Folders keep their full name.</summary>
    public string Stem => IsDirectory ? Name : LocalFileSystem.SplitExtension(Name).Stem;
}
