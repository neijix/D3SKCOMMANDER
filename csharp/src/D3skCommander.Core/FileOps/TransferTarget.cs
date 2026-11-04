namespace D3skCommander.Core.FileOps;

/// <summary>A destination folder and the optional new name of a single source.</summary>
public readonly struct TransferTarget
{
    public TransferTarget(string directory, string? renameTo)
    {
        ArgumentNullException.ThrowIfNull(directory);
        Directory = directory;
        RenameTo = renameTo;
    }

    public string Directory { get; }

    public string? RenameTo { get; }
}
