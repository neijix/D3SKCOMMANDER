using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

/// <summary>
/// One location a panel can browse: local disk, an archive, a list, or a remote site.
/// Listing streams entries and stops when cancelled. A failure is an <see cref="OperationError"/>,
/// not a thrown exception. When <see cref="IsReadOnly"/> is set, create, write, rename and delete
/// return <see cref="ErrorKind.Unsupported"/> (<see cref="FileSystemProvider.ReadOnlyMessage"/>).
/// </summary>
public interface IFileSystemProvider
{
    /// <summary>Create, write, rename and delete are refused.</summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Streams <paramref name="directory"/> one entry at a time.
    /// A failure to open it, or cancellation, is one failed item and then the stream ends.
    /// Nothing is yielded after that item.
    /// </summary>
    IAsyncEnumerable<FileResult<Entry>> ListAsync(
        string directory,
        bool showHidden,
        CancellationToken cancellationToken);

    /// <summary>One path. A missing path is <see cref="ErrorKind.NotFound"/>.</summary>
    Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken);

    /// <summary>Opens a file for reading. A directory is <see cref="ErrorKind.InvalidData"/>.</summary>
    Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Creates or truncates a file and opens it for writing. The caller owns the stream.</summary>
    Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken);

    /// <summary>Creates one folder. The parent must already exist. An existing path is refused.</summary>
    Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken);

    /// <summary>Renames an entry inside its folder. <paramref name="newName"/> is a name, not a path.</summary>
    Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken);

    /// <summary>Removes a file, a folder tree, or a link. A link's target is left in place.</summary>
    Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken);
}
