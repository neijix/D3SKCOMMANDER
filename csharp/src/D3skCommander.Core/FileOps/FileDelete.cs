using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.FileOps;

/// <summary>Permanent delete. A link is removed itself; the provider does not follow it.</summary>
internal static class FileDelete
{
    public static async Task<ItemResult> RunAsync(
        TransferWork work,
        string source,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var count = await TreeCount.CountAsync(work.Source, source, cancellationToken).ConfigureAwait(false);
        work.Session.AddTotals(count.Files, count.Bytes);
        work.Session.CurrentPath = source;
        work.Report(progress, true);
        var removed = await work.Source.DeleteAsync(source, cancellationToken).ConfigureAwait(false);
        if (removed.IsSuccess)
        {
            work.Succeed(source, null, count.Files, count.Bytes, progress);
            return ItemResult.Success;
        }

        if (removed.Error is not null && removed.Error.Kind == ErrorKind.Cancelled)
        {
            work.Abort();
        }

        var error = removed.Error ?? OperationError.NotFound(source);
        work.Fail(source, null, error, progress, count.Files, count.Bytes, true);
        return ItemResult.Failed(error);
    }
}
