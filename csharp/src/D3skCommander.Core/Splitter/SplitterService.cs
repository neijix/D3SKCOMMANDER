using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Splitter;

/// <summary>Runs split and combine on the job service. Cancel removes a partial output.</summary>
public sealed class SplitterService
{
    private readonly IJobService _jobs;

    public SplitterService(IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        _jobs = jobs;
    }

    public SplitOperation Split(
        string source,
        string destinationDirectory,
        ulong partSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        var operation = new SplitOperation();
        Start(operation, "Split", source, cancellationToken, (progress, token) =>
            SplitOne(operation, source, destinationDirectory, partSize, progress, token, cancellationToken));
        return operation;
    }

    public CombineOperation Combine(
        string anyPart,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(anyPart);
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        var operation = new CombineOperation();
        Start(operation, "Combine", anyPart, cancellationToken, (progress, token) =>
            CombineOne(operation, anyPart, destinationDirectory, progress, token, cancellationToken));
        return operation;
    }

    private void Start(
        SplitterRun run,
        string title,
        string itemName,
        CancellationToken cancellationToken,
        Func<IJobProgress, CancellationToken, ItemResult> work)
    {
        var job = _jobs.Start(title, [new JobItem(itemName)], (_, progress, token) =>
        {
            try
            {
                run.WaitUntilBound();
                StopWhen(run, cancellationToken, token);
                return Task.FromResult(work(progress, token));
            }
            finally
            {
                run.Release();
            }
        });
        run.Bind(job);
    }

    private static ItemResult SplitOne(
        SplitOperation operation,
        string source,
        string destinationDirectory,
        ulong partSize,
        IJobProgress progress,
        CancellationToken jobToken,
        CancellationToken cancellationToken)
    {
        var split = FileSplitter.Split(source, destinationDirectory, partSize, (done, total) =>
        {
            Report(progress, done, total, source);
            return StillRunning(jobToken, cancellationToken);
        });
        if (split.IsSuccess)
        {
            operation.SetParts(split.Value);
            return ItemResult.Success;
        }

        return Stop(operation, split.Error!, jobToken);
    }

    private static ItemResult CombineOne(
        CombineOperation operation,
        string anyPart,
        string destinationDirectory,
        IJobProgress progress,
        CancellationToken jobToken,
        CancellationToken cancellationToken)
    {
        var combined = FileSplitter.Combine(anyPart, destinationDirectory, (done, total) =>
        {
            Report(progress, done, total, anyPart);
            return StillRunning(jobToken, cancellationToken);
        });
        if (!combined.IsSuccess)
        {
            return Stop(operation, combined.Error!, jobToken);
        }

        operation.SetReport(combined.Value);
        if (combined.Value.CrcOk == false)
        {
            var mismatch = OperationError.InvalidData(
                $"{combined.Value.Output}: CRC mismatch", combined.Value.Output);
            operation.SetError(mismatch);
            return ItemResult.Failed(mismatch, retryable: false);
        }

        return ItemResult.Success;
    }

    private static ItemResult Stop(SplitterRun run, OperationError error, CancellationToken jobToken)
    {
        run.SetError(error);
        if (error.Kind != ErrorKind.Cancelled)
        {
            return ItemResult.Failed(error, error.Kind != ErrorKind.InvalidData);
        }

        run.Job.Cancel();
        throw new OperationCanceledException(jobToken);
    }

    private static void StopWhen(SplitterRun run, CancellationToken cancellationToken, CancellationToken jobToken)
    {
        if (!cancellationToken.IsCancellationRequested && !jobToken.IsCancellationRequested)
        {
            return;
        }

        run.Job.Cancel();
        throw new OperationCanceledException(jobToken);
    }

    private static bool StillRunning(CancellationToken jobToken, CancellationToken cancellationToken) =>
        !jobToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested;

    private static void Report(IJobProgress progress, ulong done, ulong total, string name)
    {
        var current = progress.Current;
        progress.Report(new JobProgress(
            current.ItemsDone,
            Math.Max(current.ItemsTotal, 1),
            ToLong(done),
            Math.Max(current.BytesTotal, ToLong(total)),
            name));
    }

    private static long ToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;
}
