namespace D3skCommander.Core.Search;

/// <summary>Everything the user types or ticks in the search form, as text. <see cref="SearchQueryBuilder"/> parses it.</summary>
public sealed class SearchForm
{
    /// <summary>One or more folders separated by ';'.</summary>
    public string Roots { get; set; } = "";

    public string Name { get; set; } = "*";

    public bool NameIsRegex { get; set; }

    public string Text { get; set; } = "";

    public bool TextIsRegex { get; set; }

    public bool CaseSensitive { get; set; }

    public bool WholeWords { get; set; }

    public bool NotContaining { get; set; }

    /// <summary>Masks separated by ';': a file or folder whose name matches is never a hit.</summary>
    public string ExcludeFiles { get; set; } = "";

    /// <summary>Folder name masks or absolute paths separated by ';': such a folder is not entered.</summary>
    public string ExcludeFolders { get; set; } = "";

    /// <summary>Walk into links to folders (local folders only; each real folder once).</summary>
    public bool FollowLinks { get; set; }

    public bool DepthLimited { get; set; }

    public int Depth { get; set; } = 1;

    public bool IncludeHidden { get; set; }

    public bool Files { get; set; } = true;

    public bool Dirs { get; set; } = true;

    public bool InArchives { get; set; }

    public string MinSize { get; set; } = "";

    public string MaxSize { get; set; } = "";

    /// <summary>Modified within the last N days.</summary>
    public string NewerDays { get; set; } = "";

    /// <summary>Modified more than N days ago.</summary>
    public string OlderDays { get; set; } = "";

    /// <summary>Group the files found by <see cref="DupName"/>, <see cref="DupSize"/> and/or <see cref="DupContent"/>.</summary>
    public bool FindDuplicates { get; set; }

    public bool DupName { get; set; }

    public bool DupSize { get; set; } = true;

    public bool DupContent { get; set; } = true;

    /// <summary>Confirm equal hashes byte by byte.</summary>
    public bool DupCompareBytes { get; set; }

    /// <summary>An independent copy, so a saved template never shares state with the live form.</summary>
    public SearchForm Clone() => (SearchForm)MemberwiseClone();
}
