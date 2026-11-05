namespace D3skCommander.Core.Archives;

/// <summary>One archive member. <see cref="Path"/> is <c>/</c>-separated and has no leading slash.</summary>
public sealed class ArchiveEntry
{
    public ArchiveEntry(string path, long size, long? compressedSize, bool isDirectory, DateTimeOffset? modified)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (compressedSize is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(compressedSize));
        }

        Path = path;
        Size = size;
        CompressedSize = compressedSize;
        IsDirectory = isDirectory;
        Modified = modified;
    }

    public string Path { get; }

    public long Size { get; }

    public long? CompressedSize { get; }

    public bool IsDirectory { get; }

    public DateTimeOffset? Modified { get; }
}
