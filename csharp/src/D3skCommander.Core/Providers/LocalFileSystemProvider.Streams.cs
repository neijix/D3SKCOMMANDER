using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

public sealed partial class LocalFileSystemProvider
{
    protected override Task<FileResult<Stream>> OpenReadCore(string path, CancellationToken cancellationToken)
    {
        if (Cancelled(cancellationToken, path, out var error))
        {
            return StreamResult(FileResult<Stream>.Fail(error!));
        }

        if (Directory.Exists(path))
        {
            return StreamResult(FileResult<Stream>.Fail(OperationError.InvalidData("not a file", path)));
        }

        try
        {
            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return StreamResult(FileResult<Stream>.Ok(stream));
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return StreamResult(FileResult<Stream>.Fail(OperationError.FromException(ex, path)));
        }
    }

    protected override Task<FileResult<Stream>> OpenWriteCore(string path, CancellationToken cancellationToken)
    {
        if (Cancelled(cancellationToken, path, out var error))
        {
            return StreamResult(FileResult<Stream>.Fail(error!));
        }

        if (Directory.Exists(path))
        {
            return StreamResult(FileResult<Stream>.Fail(OperationError.InvalidData("not a file", path)));
        }

        try
        {
            Stream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            return StreamResult(FileResult<Stream>.Ok(stream));
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return StreamResult(FileResult<Stream>.Fail(OperationError.FromException(ex, path)));
        }
    }
}
