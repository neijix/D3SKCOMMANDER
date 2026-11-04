namespace D3skCommander.Core.FileOps;

/// <summary>Both sides of one existing target, for the overwrite dialog.</summary>
public sealed class ConflictQuestion
{
    public ConflictQuestion(
        string sourcePath,
        long sourceSize,
        DateTimeOffset? sourceModified,
        string targetPath,
        long targetSize,
        DateTimeOffset? targetModified)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(targetPath);
        SourcePath = sourcePath;
        SourceSize = sourceSize;
        SourceModified = sourceModified;
        TargetPath = targetPath;
        TargetSize = targetSize;
        TargetModified = targetModified;
    }

    public string SourcePath { get; }

    public long SourceSize { get; }

    public DateTimeOffset? SourceModified { get; }

    public string TargetPath { get; }

    public long TargetSize { get; }

    public DateTimeOffset? TargetModified { get; }
}
