namespace D3skCommander.Core.Search;

/// <summary>
/// What to look for. <see cref="MaxDepth"/> counts levels below a root: 1 = direct children only.
/// </summary>
public sealed class SearchQuery
{
    public IReadOnlyList<string> Roots { get; init; } = [];

    /// <summary>A wildcard mask (see <c>Mask</c>) or, with <see cref="NameIsRegex"/>, a regex searched in the name.</summary>
    public string Name { get; init; } = "*";

    public bool NameIsRegex { get; init; }

    /// <summary>Text or regex looked for inside files; null or empty means no content test.</summary>
    public string? Content { get; init; }

    public bool ContentIsRegex { get; init; }

    /// <summary>Applies to the content and to a regex name; a mask name is always case-insensitive.</summary>
    public bool CaseSensitive { get; init; }

    public bool WholeWords { get; init; }

    /// <summary>Keep the files that do NOT contain <see cref="Content"/>.</summary>
    public bool NotContaining { get; init; }

    /// <summary>Masks separated by ';' (see <c>Mask</c>): a file or folder named like one is not a hit. Empty = none.</summary>
    public string ExcludeFiles { get; init; } = "";

    /// <summary>Folder name masks, or absolute paths (that folder and what is below it): never entered.</summary>
    public IReadOnlyList<string> ExcludeFolders { get; init; } = [];

    /// <summary>Walk into links to folders on the local file system; each real folder is visited once.</summary>
    public bool FollowLinks { get; init; }

    public int? MaxDepth { get; init; }

    public bool IncludeHidden { get; init; }

    /// <summary>Size bounds apply to files only.</summary>
    public ulong? MinSize { get; init; }

    public ulong? MaxSize { get; init; }

    /// <summary>Modified strictly after this instant.</summary>
    public DateTimeOffset? NewerThan { get; init; }

    /// <summary>Modified strictly before this instant.</summary>
    public DateTimeOffset? OlderThan { get; init; }

    public bool Files { get; init; } = true;

    public bool Dirs { get; init; } = true;

    /// <summary>Look inside ZIP, TAR, TAR.GZ and 7Z files through the archive provider.</summary>
    public bool SearchArchives { get; init; }
}
