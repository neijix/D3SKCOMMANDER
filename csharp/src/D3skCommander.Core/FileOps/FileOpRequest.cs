using D3skCommander.Core.Providers;

namespace D3skCommander.Core.FileOps;

/// <summary>One copy, move, or delete: the sources and, except for delete, where they go.</summary>
public sealed class FileOpRequest
{
    public FileOpRequest(
        FileOpKind kind,
        IFileSystemProvider source,
        IReadOnlyList<string> sources,
        IFileSystemProvider? destination = null,
        string? destinationDirectory = null,
        string? renameTo = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sources);
        Kind = kind;
        Source = source;
        Sources = Copy(sources);
        Destination = destination;
        DestinationDirectory = destinationDirectory;
        RenameTo = renameTo;
    }

    public FileOpKind Kind { get; }

    public IFileSystemProvider Source { get; }

    public IReadOnlyList<string> Sources { get; }

    public IFileSystemProvider? Destination { get; }

    /// <summary>Folder the sources land in. Null for delete, and an error for copy or move.</summary>
    public string? DestinationDirectory { get; }

    /// <summary>New name of a single source. Ignored when there is not exactly one source.</summary>
    public string? RenameTo { get; }

    private static string[] Copy(IReadOnlyList<string> sources)
    {
        var copy = new string[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            var path = sources[i];
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A source path is empty.", nameof(sources));
            }

            copy[i] = path;
        }

        return copy;
    }
}
