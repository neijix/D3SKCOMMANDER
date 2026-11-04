using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.FileOps;

/// <summary>Creates a folder, including missing parents, the way v0.1's <c>create_dir_all</c> did.</summary>
internal static class FileNewFolder
{
    public static async Task<ItemResult> RunAsync(
        FileOperationService service,
        FileOpSession session,
        IOperationLog log,
        IFileSystemProvider provider,
        string path,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var job = service.FindRunning(path);
        if (cancellationToken.IsCancellationRequested)
        {
            session.MarkCancelled();
            job.Cancel();
            throw new OperationCanceledException();
        }

        session.CurrentPath = path;
        var error = await FolderChain.EnsureAsync(provider, path, cancellationToken).ConfigureAwait(false);
        if (error is null)
        {
            FileOpLog.Write(log, "MKDIR", path, null, null);
            session.Report(progress, null, true);
            return ItemResult.Success;
        }

        if (error.Kind == ErrorKind.Cancelled)
        {
            session.MarkCancelled();
            job.Cancel();
            throw new OperationCanceledException();
        }

        session.AddError(error);
        FileOpLog.Write(log, "MKDIR", path, null, error.Message);
        session.Report(progress, null, true);
        return ItemResult.Failed(error);
    }
}
