namespace D3skCommander.Core.Panels;

/// <summary>What a right-click in a panel landed on.</summary>
public enum ContextTargetKind
{
    File,
    ArchiveFile,
    Folder,

    /// <summary>Empty space below the rows, or the <c>..</c> row.</summary>
    EmptySpace,
}

/// <summary>Which kind of listing the panel shows.</summary>
public enum PanelProviderKind
{
    Local,

    /// <summary>Search results or a branch view: one flat list of entries from many folders.</summary>
    FlatList,

    Archive,

    /// <summary>FTP or another remote provider.</summary>
    Remote,
}

/// <summary>
/// The target of a context menu: the kind of row, the panel's provider kind and folder, the entry
/// under the click (null for <see cref="ContextTargetKind.EmptySpace"/>) and the marked paths.
/// Pure: no file system access.
/// </summary>
public sealed record PanelContextTarget(
    ContextTargetKind Kind,
    PanelProviderKind Provider,
    string PanelFolder,
    string? EntryPath,
    IReadOnlyList<string> MarkedPaths)
{
    /// <summary>
    /// The folder a "here" command (terminal, PowerShell, Windows menu) works in: a file's folder,
    /// the folder itself, or the panel folder for empty space and the <c>..</c> row.
    /// </summary>
    public string HereFolder
    {
        get
        {
            if (Kind == ContextTargetKind.EmptySpace || string.IsNullOrEmpty(EntryPath))
            {
                return PanelFolder;
            }

            if (Kind == ContextTargetKind.Folder)
            {
                return EntryPath;
            }

            return FolderOf(EntryPath) ?? PanelFolder;
        }
    }

    /// <summary>True when the marked paths (none or one included) all sit in the same folder.</summary>
    public bool MarkedInOneFolder
    {
        get
        {
            if (MarkedPaths.Count < 2)
            {
                return true;
            }

            var first = FolderOf(MarkedPaths[0]);
            return MarkedPaths.All(p => string.Equals(FolderOf(p), first, StringComparison.Ordinal));
        }
    }

    // Accepts both separators and keeps the path's own; a root entry gives the root ("/", "C:\").
    private static string? FolderOf(string path)
    {
        var trimmed = path.Length > 1 ? path.TrimEnd('/', '\\') : path;
        var cut = trimmed.LastIndexOfAny(['/', '\\']);
        if (cut < 0)
        {
            return null;
        }

        if (cut == 0 || (cut == 2 && trimmed[1] == ':'))
        {
            return trimmed[..(cut + 1)];
        }

        return trimmed[..cut];
    }
}
