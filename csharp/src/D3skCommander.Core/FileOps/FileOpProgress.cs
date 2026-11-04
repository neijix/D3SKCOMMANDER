namespace D3skCommander.Core.FileOps;

/// <summary>
/// Running totals. <see cref="DoneFiles"/> counts every file handled
/// (copied, skipped, or failed) so the bar can finish.
/// </summary>
public sealed class FileOpProgress
{
    public FileOpProgress(string currentPath, int doneFiles, int totalFiles, long doneBytes, long totalBytes)
    {
        ArgumentNullException.ThrowIfNull(currentPath);
        ArgumentOutOfRangeException.ThrowIfNegative(doneFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(totalFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(doneBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytes);
        CurrentPath = currentPath;
        DoneFiles = doneFiles;
        TotalFiles = totalFiles;
        DoneBytes = doneBytes;
        TotalBytes = totalBytes;
    }

    public string CurrentPath { get; }

    public int DoneFiles { get; }

    public int TotalFiles { get; }

    public long DoneBytes { get; }

    public long TotalBytes { get; }
}
