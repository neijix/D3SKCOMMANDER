using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Ftp;

namespace D3skCommander.App.Features.Ftp;

/// <summary>The real network: <see cref="FtpConnection"/> behind <see cref="IFtpSession"/>.</summary>
public sealed class FtpGateway : IFtpGateway
{
    public async Task<FileResult<IFtpSession>> ConnectAsync(FtpSite site, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(password);
        var connected = await FtpConnection.ConnectAsync(site, password, null, cancellationToken).ConfigureAwait(false);
        if (!connected.IsSuccess)
        {
            return FileResult<IFtpSession>.Fail(connected.Error!);
        }

        return FileResult<IFtpSession>.Ok(new Session(connected.Value));
    }

    private sealed class Session : IFtpSession
    {
        private readonly FtpConnection _connection;

        public Session(FtpConnection connection)
        {
            _connection = connection;
        }

        public bool IsConnected => _connection.IsConnected;

        public Task<FileResult<string>> WorkingDirectoryAsync(CancellationToken cancellationToken) =>
            _connection.WorkingDirectoryAsync(cancellationToken);

        public async Task<FileResult<IReadOnlyList<Entry>>> ListAsync(string directory, CancellationToken cancellationToken)
        {
            var listed = await _connection.ListAsync(directory, cancellationToken).ConfigureAwait(false);
            if (!listed.IsSuccess)
            {
                return FileResult<IReadOnlyList<Entry>>.Fail(listed.Error!);
            }

            var sorted = new List<RemoteEntry>(listed.Value);
            FtpPaths.Sort(sorted);
            var entries = new List<Entry>(sorted.Count);
            foreach (var remote in sorted)
            {
                entries.Add(FtpFileSystemProvider.ToEntry(remote, FtpPaths.Join(directory, remote.Name)));
            }

            return FileResult<IReadOnlyList<Entry>>.Ok(entries);
        }

        public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
            _connection.CreateFolderAsync(path, cancellationToken);

        public Task<FileResult<FtpTransfer>> DownloadAsync(string remote, string local, IProgress<long>? progress, CancellationToken cancellationToken) =>
            _connection.DownloadAsync(remote, local, progress, cancellationToken);

        public Task<FileResult<FtpTransfer>> UploadAsync(string local, string remote, IProgress<long>? progress, CancellationToken cancellationToken) =>
            _connection.UploadAsync(local, remote, progress, cancellationToken);

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
