namespace D3skCommander.Core.Sync;

/// <summary>How two directories are compared. Defaults match v0.1.</summary>
public sealed class SyncOptions
{
    /// <summary>Descend into subdirectories.</summary>
    public bool Subdirs { get; set; } = true;

    /// <summary>Decide "equal" by comparing the bytes, not size and date.</summary>
    public bool ByContent { get; set; }

    /// <summary>The same size counts as equal whatever the dates say.</summary>
    public bool IgnoreDate { get; set; }

    /// <summary>File-name mask. Directories are never filtered by it.</summary>
    public string Mask { get; set; } = "*";

    /// <summary>List directories that exist, empty, on one side only.</summary>
    public bool EmptyDirs { get; set; } = true;

    /// <summary>The right side becomes a mirror of the left one.</summary>
    public bool Asymmetric { get; set; }

    /// <summary>Dates closer than this are equal. Two seconds: FAT stores even seconds.</summary>
    public ulong TimeToleranceSecs { get; set; } = 2;
}
