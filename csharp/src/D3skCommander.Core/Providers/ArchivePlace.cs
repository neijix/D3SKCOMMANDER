using D3skCommander.Core.Common;

namespace D3skCommander.Core.Providers;

/// <summary>An archive file plus the folder inside it. An empty <see cref="Inner"/> is the archive root.</summary>
public readonly struct ArchivePlace
{
    private ArchivePlace(string? archive, string inner, OperationError? error)
    {
        Archive = archive;
        Inner = inner;
        Error = error;
    }

    public string? Archive { get; }

    /// <summary><c>/</c>-separated path inside the archive. Empty at the archive root.</summary>
    public string Inner { get; }

    public OperationError? Error { get; }

    public static ArchivePlace Found(string archive, string inner) => new(archive, inner, null);

    public static ArchivePlace Missing(string path) => new(null, "", OperationError.NotFound(path));
}
