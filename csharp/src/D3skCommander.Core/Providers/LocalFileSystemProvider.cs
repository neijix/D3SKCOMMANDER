using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

/// <summary>The local disk. Listing, entry info and rename go through <see cref="LocalFileSystem"/>.</summary>
public sealed partial class LocalFileSystemProvider : FileSystemProvider
{
    public override bool IsReadOnly => false;

    private static bool IsFileSystem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static Task<FileStatus> Done() => Task.FromResult(FileStatus.Success);

    private static Task<FileStatus> Status(OperationError error) => Task.FromResult(FileStatus.Fail(error));

    private static Task<FileResult<Stream>> StreamResult(FileResult<Stream> result) => Task.FromResult(result);

    private static bool Cancelled(CancellationToken cancellationToken, string path, out OperationError? error)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            error = OperationError.Cancelled(path);
            return true;
        }

        error = null;
        return false;
    }
}
