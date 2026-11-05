using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

public sealed partial class ListFileSystemProvider
{
    protected override Task<FileResult<Stream>> OpenWriteCore(string path, CancellationToken cancellationToken)
    {
        if (Same(path, _directory))
        {
            return Task.FromResult(FileResult<Stream>.Fail(OperationError.InvalidData("not a file", path)));
        }

        return _local.OpenWriteAsync(path, cancellationToken);
    }

    protected override Task<FileStatus> CreateFolderCore(string path, CancellationToken cancellationToken) =>
        _local.CreateFolderAsync(path, cancellationToken);

    protected override Task<FileResult<string>> RenameCore(string path, string newName, CancellationToken cancellationToken) =>
        _local.RenameAsync(path, newName, cancellationToken);

    protected override Task<FileStatus> DeleteCore(string path, CancellationToken cancellationToken) =>
        _local.DeleteAsync(path, cancellationToken);
}
