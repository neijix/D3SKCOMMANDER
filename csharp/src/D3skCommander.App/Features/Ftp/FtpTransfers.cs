using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Ftp;
using D3skCommander.Core.Jobs;

namespace D3skCommander.App.Features.Ftp;

/// <summary>
/// One transfer job's work: files and folders (recursively, to a depth limit) between the
/// remote session and a local folder. Bytes are accumulated across the whole job.
/// </summary>
internal sealed class FtpTransfers
{
    /// <summary>How deep folders go (guards against loops), as v0.1.</summary>
    public const int MaxDepth = 24;

    private readonly IFtpSession _session;
    private long _bytes;

    public FtpTransfers(IFtpSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    public async Task<ItemResult> DownloadAsync(
        Entry remote,
        string localFolder,
        int itemsDone,
        int itemsTotal,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var run = new Run(progress, itemsDone, itemsTotal, this);
        var error = await DownloadEntryAsync(remote, localFolder, 0, run, cancellationToken).ConfigureAwait(false);
        return error is null ? ItemResult.Success : ItemResult.Failed(error, error.Kind != ErrorKind.Cancelled);
    }

    public async Task<ItemResult> UploadAsync(
        string local,
        string remoteFolder,
        int itemsDone,
        int itemsTotal,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var run = new Run(progress, itemsDone, itemsTotal, this);
        var error = await UploadPathAsync(local, remoteFolder, 0, run, cancellationToken).ConfigureAwait(false);
        return error is null ? ItemResult.Success : ItemResult.Failed(error, error.Kind != ErrorKind.Cancelled);
    }

    private async Task<OperationError?> DownloadEntryAsync(Entry remote, string localFolder, int depth, Run run, CancellationToken cancellationToken)
    {
        if (!FtpPaths.IsSafeName(remote.Name))
        {
            return OperationError.InvalidData("unsafe remote name: " + remote.Name, remote.Path);
        }

        var target = Path.Combine(localFolder, remote.Name);
        if (!remote.IsDirectory)
        {
            run.Name(remote.Name);
            var done = await _session.DownloadAsync(remote.Path, target, run.Bytes, cancellationToken).ConfigureAwait(false);
            return run.Finish(done);
        }

        if (depth >= MaxDepth)
        {
            return OperationError.InvalidData("folders are nested too deeply", remote.Path);
        }

        var listed = await _session.ListAsync(remote.Path, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess)
        {
            return listed.Error;
        }

        try
        {
            Directory.CreateDirectory(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationError.FromException(ex, target);
        }

        foreach (var child in listed.Value)
        {
            var error = await DownloadEntryAsync(child, target, depth + 1, run, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    private async Task<OperationError?> UploadPathAsync(string local, string remoteFolder, int depth, Run run, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(local));
        var remote = FtpPaths.Join(remoteFolder, name);
        if (!Directory.Exists(local))
        {
            run.Name(name);
            var done = await _session.UploadAsync(local, remote, run.Bytes, cancellationToken).ConfigureAwait(false);
            return run.Finish(done);
        }

        if (depth >= MaxDepth)
        {
            return OperationError.InvalidData("folders are nested too deeply", local);
        }

        var made = await _session.CreateFolderAsync(remote, cancellationToken).ConfigureAwait(false);
        if (!made.IsSuccess && made.Error!.Kind != ErrorKind.AlreadyExists)
        {
            return made.Error;
        }

        foreach (var child in Directory.EnumerateFileSystemEntries(local).Order(StringComparer.Ordinal))
        {
            var error = await UploadPathAsync(child, remote, depth + 1, run, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    /// <summary>The progress of one item: reports the shared byte count and the current name.</summary>
    private sealed class Run
    {
        private readonly IJobProgress _progress;
        private readonly int _done;
        private readonly int _total;
        private readonly FtpTransfers _owner;
        private string _name = "";
        private long _base;

        public Run(IJobProgress progress, int done, int total, FtpTransfers owner)
        {
            _progress = progress;
            _done = done;
            _total = total;
            _owner = owner;
            Bytes = new Progress<long>(Report);
        }

        public IProgress<long> Bytes { get; }

        public void Name(string name)
        {
            _name = name;
            _base = _owner._bytes;
            Report(0);
        }

        public OperationError? Finish(FileResult<FtpTransfer> result)
        {
            if (!result.IsSuccess)
            {
                return result.Error;
            }

            _owner._bytes = _base + result.Value.Bytes;
            Report(result.Value.Bytes);
            return null;
        }

        private void Report(long bytes) =>
            _progress.Report(new JobProgress(_done, _total, _base + bytes, 0, _name));
    }
}
