using System.Runtime.CompilerServices;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;

namespace D3skCommander.Core.Providers;

/// <summary>
/// A network server's root, <c>\\server\</c>: one read-only folder whose rows are the server's disk shares
/// (<see cref="IServerShares"/>), after a ".." row to Home. A share opens as an ordinary local path.
/// </summary>
public sealed class ServerFileSystemProvider : FileSystemProvider
{
    private readonly IServerShares _shares;

    public ServerFileSystemProvider(IServerShares shares)
    {
        ArgumentNullException.ThrowIfNull(shares);
        _shares = shares;
    }

    public override bool IsReadOnly => true;

    protected override async IAsyncEnumerable<FileResult<Entry>> ListCore(
        string directory,
        bool showHidden,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        var server = UncPath.Server(directory);
        if (server is null)
        {
            yield return FileResult<Entry>.Fail(OperationError.NotFound(directory));
            yield break;
        }

        var listed = _shares.Shares(server);
        if (!listed.IsSuccess)
        {
            yield return FileResult<Entry>.Fail(listed.Error ?? OperationError.NotFound(directory));
            yield break;
        }

        yield return FileResult<Entry>.Ok(new Entry("..", ComputerAddress.Text, true, false, true, 0, null, false, false, ""));
        foreach (var share in listed.Value)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return FileResult<Entry>.Fail(OperationError.Cancelled(directory));
                yield break;
            }

            yield return FileResult<Entry>.Ok(new Entry(share, UncPath.ShareRoot(server, share), true, false, false, 0, null, false, false, ""));
        }
    }

    protected override Task<FileResult<Entry>> GetEntryCore(string path, CancellationToken cancellationToken)
    {
        var server = UncPath.Server(path);
        if (server is null)
        {
            return Task.FromResult(FileResult<Entry>.Fail(OperationError.NotFound(path)));
        }

        var self = new Entry(server, UncPath.ServerRoot(server), true, false, false, 0, null, false, true, "");
        return Task.FromResult(FileResult<Entry>.Ok(self));
    }

    protected override Task<FileResult<Stream>> OpenReadCore(string path, CancellationToken cancellationToken) =>
        Task.FromResult(FileResult<Stream>.Fail(OperationError.InvalidData("a server is a folder, not a file", path)));

    protected override Task<FileResult<Stream>> OpenWriteCore(string path, CancellationToken cancellationToken) =>
        Task.FromResult(FileResult<Stream>.Fail(ReadOnly(path)));

    protected override Task<FileStatus> CreateFolderCore(string path, CancellationToken cancellationToken) =>
        Task.FromResult(FileStatus.Fail(ReadOnly(path)));

    protected override Task<FileResult<string>> RenameCore(string path, string newName, CancellationToken cancellationToken) =>
        Task.FromResult(FileResult<string>.Fail(ReadOnly(path)));

    protected override Task<FileStatus> DeleteCore(string path, CancellationToken cancellationToken) =>
        Task.FromResult(FileStatus.Fail(ReadOnly(path)));

    private static OperationError ReadOnly(string path) => OperationError.Unsupported(ReadOnlyMessage, path);
}
