using D3skCommander.Core.Common;
using D3skCommander.Core.Compare;

namespace D3skCommander.Core.Sync;

internal static class SyncRules
{
    public static SyncAction DefaultAction(SyncStatus status, bool asymmetric)
    {
        if (status == SyncStatus.Equal)
        {
            return SyncAction.None;
        }

        if (status == SyncStatus.LeftOnly || status == SyncStatus.LeftNewer)
        {
            return SyncAction.CopyToRight;
        }

        if (status == SyncStatus.RightOnly)
        {
            return asymmetric ? SyncAction.DeleteRight : SyncAction.CopyToLeft;
        }

        if (status == SyncStatus.Different)
        {
            return asymmetric ? SyncAction.CopyToRight : SyncAction.None;
        }

        if (status == SyncStatus.RightNewer)
        {
            return asymmetric ? SyncAction.CopyToRight : SyncAction.CopyToLeft;
        }

        throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown sync status.");
    }

    public static StatusResult Classify(
        SyncSide left,
        SyncSide right,
        string leftPath,
        string rightPath,
        SyncOptions options)
    {
        var delta = Delta(left.Modified, right.Modified);
        var sameDate = Magnitude(delta) <= options.TimeToleranceSecs;
        if (left.Size != right.Size)
        {
            return Status(Decide(false, sameDate, delta, options));
        }

        if (!options.ByContent)
        {
            return Status(Decide(options.IgnoreDate || sameDate, sameDate, delta, options));
        }

        try
        {
            var same = FileCompare.FilesIdentical(leftPath, rightPath);
            return Status(Decide(same, sameDate, delta, options));
        }
        catch (Exception ex) when (SyncIo.IsFileSystem(ex))
        {
            return StatusResult.Fail(OperationError.FromException(ex, leftPath));
        }
    }

    private static SyncStatus Decide(bool sameContent, bool sameDate, long delta, SyncOptions options)
    {
        if (sameContent)
        {
            return SyncStatus.Equal;
        }

        if (options.IgnoreDate || sameDate)
        {
            return SyncStatus.Different;
        }

        if (delta > 0)
        {
            return SyncStatus.LeftNewer;
        }

        return SyncStatus.RightNewer;
    }

    private static long Delta(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null || right is null)
        {
            return 0;
        }

        return left.Value.ToUnixTimeSeconds() - right.Value.ToUnixTimeSeconds();
    }

    private static ulong Magnitude(long delta)
    {
        if (delta >= 0)
        {
            return (ulong)delta;
        }

        return (ulong)(-delta);
    }

    private static StatusResult Status(SyncStatus status) => StatusResult.Ok(status);
}

internal readonly struct StatusResult
{
    private StatusResult(SyncStatus status, OperationError? error)
    {
        Status = status;
        Error = error;
    }

    public SyncStatus Status { get; }

    public OperationError? Error { get; }

    public static StatusResult Ok(SyncStatus status) => new(status, null);

    public static StatusResult Fail(OperationError error) => new(default, error);
}
