using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.FileOps;

internal static partial class FileCopy
{
    private static async Task CopyFileAsync(
        TransferWork work,
        string source,
        Entry entry,
        string target,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var size = TreeCount.ToLong(entry.Size);
        var destination = await work.ResolveFileTargetAsync(source, entry, target, size, progress, cancellationToken)
            .ConfigureAwait(false);
        if (destination is null)
        {
            return;
        }

        work.Session.CurrentPath = source;
        work.Report(progress, true);
        if (MovedInPlace(work, source, destination, size, progress))
        {
            return;
        }

        var start = work.Session.DoneBytes;
        var error = await WriteFileAsync(work, source, destination, progress, cancellationToken).ConfigureAwait(false);
        if (error is null)
        {
            await work.FinishCopiedFileAsync(source, destination, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (error.Kind == ErrorKind.Cancelled)
        {
            work.Abort();
        }

        work.Session.DoneBytes = start;
        work.Fail(source, destination, error, progress, 1, size, true);
    }

    private static bool MovedInPlace(
        TransferWork work,
        string source,
        string destination,
        long size,
        IJobProgress progress)
    {
        if (!work.Moving || !FileMove.TryRename(work, source, destination, false, true))
        {
            return false;
        }

        work.Succeed(source, destination, 1, size, progress);
        return true;
    }

    private static async Task CopyLinkAsync(
        TransferWork work,
        string source,
        Entry entry,
        string target,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var destination = await work.ResolveFileTargetAsync(source, entry, target, 0, progress, cancellationToken)
            .ConfigureAwait(false);
        if (destination is null)
        {
            return;
        }

        work.Session.CurrentPath = source;
        work.Report(progress, true);
        if (MovedInPlace(work, source, destination, 0, progress))
        {
            return;
        }

        var error = await CreateLinkAsync(work, source, destination).ConfigureAwait(false);
        if (error is null)
        {
            await work.FinishCopiedFileAsync(source, destination, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        work.Fail(source, destination, error, progress, 1, 0, true);
    }

    private static async Task<OperationError?> CreateLinkAsync(TransferWork work, string source, string destination)
    {
        var link = ReadLink(source);
        if (link.Error is not null)
        {
            return link.Error;
        }

        var removed = await work.Destination.DeleteAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!removed.IsSuccess && removed.Error is not null && removed.Error.Kind != ErrorKind.NotFound)
        {
            return removed.Error;
        }

        try
        {
            File.CreateSymbolicLink(destination, link.Target ?? "");
            return null;
        }
        catch (Exception ex) when (LocalMetadata.IsFileSystem(ex))
        {
            return OperationError.FromException(ex, destination);
        }
    }

    private static (string? Target, OperationError? Error) ReadLink(string source)
    {
        try
        {
            var target = new FileInfo(source).LinkTarget;
            if (string.IsNullOrEmpty(target))
            {
                return (null, OperationError.Other("the link has no target", source));
            }

            return (target, null);
        }
        catch (Exception ex) when (LocalMetadata.IsFileSystem(ex))
        {
            return (null, OperationError.FromException(ex, source));
        }
    }
}
