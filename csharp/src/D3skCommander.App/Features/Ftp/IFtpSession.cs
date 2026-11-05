using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Ftp;

namespace D3skCommander.App.Features.Ftp;

/// <summary>Opens a connection to a site. The window talks to this so a test can stand in for the network.</summary>
public interface IFtpGateway
{
    Task<FileResult<IFtpSession>> ConnectAsync(FtpSite site, string password, CancellationToken cancellationToken);
}

/// <summary>What the window needs of a connected site: listing, a folder, and one file each way.</summary>
public interface IFtpSession : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>The current remote folder (the start folder right after connecting).</summary>
    Task<FileResult<string>> WorkingDirectoryAsync(CancellationToken cancellationToken);

    /// <summary>The entries of <paramref name="directory"/>, folders first, each with its absolute remote path.</summary>
    Task<FileResult<IReadOnlyList<Entry>>> ListAsync(string directory, CancellationToken cancellationToken);

    Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken);

    Task<FileResult<FtpTransfer>> DownloadAsync(string remote, string local, IProgress<long>? progress, CancellationToken cancellationToken);

    Task<FileResult<FtpTransfer>> UploadAsync(string local, string remote, IProgress<long>? progress, CancellationToken cancellationToken);
}
