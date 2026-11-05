using System.Runtime.CompilerServices;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Ftp;

/// <summary>
/// A connected FTP or FTPS site as a place a panel can browse. Paths are absolute remote paths
/// (<see cref="FtpPaths.Join"/>). The provider does not own the connection: whoever connected closes it.
/// A stream from <see cref="IFileSystemProvider.OpenReadAsync"/> or <c>OpenWriteAsync</c> keeps the
/// connection busy until it is disposed, so other calls wait for it.
/// </summary>
public sealed class FtpFileSystemProvider : FileSystemProvider
{
    public FtpFileSystemProvider(FtpConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Connection = connection;
    }

    public FtpConnection Connection { get; }

    public override bool IsReadOnly => false;

    protected override async IAsyncEnumerable<FileResult<Entry>> ListCore(
        string directory,
        bool showHidden,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var listed = await Connection.ListAsync(directory, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess)
        {
            yield return FileResult<Entry>.Fail(listed.Error!);
            yield break;
        }

        foreach (var remote in listed.Value)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return FileResult<Entry>.Fail(OperationError.Cancelled(directory));
                yield break;
            }

            if (showHidden || !remote.Name.StartsWith('.'))
            {
                yield return FileResult<Entry>.Ok(ToEntry(remote, FtpPaths.Join(directory, remote.Name)));
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            yield return FileResult<Entry>.Fail(OperationError.Cancelled(directory));
        }
    }

    protected override async Task<FileResult<Entry>> GetEntryCore(string path, CancellationToken cancellationToken)
    {
        var found = await Connection.GetEntryAsync(path, cancellationToken).ConfigureAwait(false);
        return found.IsSuccess
            ? FileResult<Entry>.Ok(ToEntry(found.Value, path))
            : FileResult<Entry>.Fail(found.Error!);
    }

    protected override Task<FileResult<Stream>> OpenReadCore(string path, CancellationToken cancellationToken) =>
        Connection.OpenReadAsync(path, cancellationToken);

    protected override Task<FileResult<Stream>> OpenWriteCore(string path, CancellationToken cancellationToken) =>
        Connection.OpenWriteAsync(path, cancellationToken);

    protected override Task<FileStatus> CreateFolderCore(string path, CancellationToken cancellationToken) =>
        Connection.CreateFolderAsync(path, cancellationToken);

    protected override Task<FileResult<string>> RenameCore(string path, string newName, CancellationToken cancellationToken) =>
        Connection.RenameAsync(path, newName, cancellationToken);

    protected override Task<FileStatus> DeleteCore(string path, CancellationToken cancellationToken) =>
        Connection.DeleteAsync(path, cancellationToken);

    /// <summary>A remote listing entry as a panel entry at <paramref name="path"/>; a dot name is hidden.</summary>
    public static Entry ToEntry(RemoteEntry remote, string path) =>
        new(
            remote.Name,
            path,
            remote.IsDirectory,
            isLink: false,
            isParent: false,
            remote.Size,
            remote.Modified,
            hidden: remote.Name.StartsWith('.'),
            readOnly: false,
            remote.Permissions);
}
