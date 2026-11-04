using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.FileOps;

internal sealed partial class TransferWork
{
    public async Task<string?> ResolveFileTargetAsync(
        string source,
        Entry sourceEntry,
        string target,
        long size,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var existing = await Destination.GetEntryAsync(target, cancellationToken).ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return MissingTarget(source, target, size, existing.Error, progress);
        }

        if (existing.Value.IsDirectory && !existing.Value.IsLink)
        {
            Fail(source, target, OperationError.Other("the target is a folder", source), progress, 1, size, true);
            return null;
        }

        var decision = await _conflicts.DecideAsync(this, source, sourceEntry, target, existing.Value, cancellationToken)
            .ConfigureAwait(false);
        if (decision.Cancel)
        {
            Abort();
        }

        if (decision.Skip)
        {
            Session.Skip(1, size);
            Report(progress, true);
            return null;
        }

        return decision.Path;
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        var entry = await Destination.GetEntryAsync(path, cancellationToken).ConfigureAwait(false);
        if (entry.IsSuccess)
        {
            return true;
        }

        if (entry.Error is not null && entry.Error.Kind == ErrorKind.Cancelled)
        {
            Abort();
        }

        return false;
    }

    public async Task FinishCopiedFileAsync(
        string source,
        string destination,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        if (!Moving)
        {
            Succeed(source, destination, 1, 0, progress);
            return;
        }

        var removed = await Source.DeleteAsync(source, cancellationToken).ConfigureAwait(false);
        if (removed.IsSuccess)
        {
            Succeed(source, destination, 1, 0, progress);
            return;
        }

        if (removed.Error is not null && removed.Error.Kind == ErrorKind.Cancelled)
        {
            Abort();
        }

        var detail = removed.Error is null ? "unknown" : removed.Error.Message;
        var error = OperationError.Other("copied, but the source could not be removed: " + detail, source);
        Fail(source, destination, error, progress, 1, 0, true);
    }

    public async Task RemovePartialAsync(string path)
    {
        var entry = await Destination.GetEntryAsync(path, CancellationToken.None).ConfigureAwait(false);
        if (!entry.IsSuccess || (entry.Value.IsDirectory && !entry.Value.IsLink))
        {
            return;
        }

        await Destination.DeleteAsync(path, CancellationToken.None).ConfigureAwait(false);
    }

    private string? MissingTarget(
        string source,
        string target,
        long size,
        OperationError? error,
        IJobProgress progress)
    {
        if (error is not null && error.Kind == ErrorKind.Cancelled)
        {
            Abort();
        }

        if (error is null || error.Kind == ErrorKind.NotFound)
        {
            return target;
        }

        Fail(source, target, error, progress, 1, size, true);
        return null;
    }
}
