namespace D3skCommander.Core.Viewer;

/// <summary>Caps from the v0.1 lister (<c>src/ui/lister/text.rs</c> and <c>table.rs</c>).</summary>
public static class ViewerLimits
{
    /// <summary>Bytes read at most. A larger file is marked truncated.</summary>
    public const int MaxRead = 16 * 1024 * 1024;

    /// <summary>A NUL in this many leading bytes marks the file as binary.</summary>
    public const int Sniff = 8 * 1024;

    /// <summary>Bytes per hex row.</summary>
    public const int HexWidth = 16;

    /// <summary>Unwrapped lines longer than this are cut for display.</summary>
    public const int MaxLineChars = 2000;

    /// <summary>DBF records loaded at most.</summary>
    public const int MaxDbfRecords = 5000;

    /// <summary>A larger catalog is shown as bytes instead of being parsed.</summary>
    public const long MaxCatalogSize = 64L * 1024 * 1024;
}
