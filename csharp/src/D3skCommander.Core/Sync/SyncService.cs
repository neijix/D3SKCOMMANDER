using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Sync;

/// <summary>Runs a reviewed sync plan as a job. Item errors stay on the report; cancel stops before the next item.</summary>
public sealed class SyncService
{
    /// <summary>English title, the command label without waiting on the localizer.</summary>
    public const string ApplyTitle = "Synchronize directories";

    private readonly IJobService _jobs;

    public SyncService(IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        _jobs = jobs;
    }

    public SyncOperation Apply(string left, string right, IReadOnlyList<SyncItem> items, string title = ApplyTitle)
    {
        ArgumentException.ThrowIfNullOrEmpty(left);
        ArgumentException.ThrowIfNullOrEmpty(right);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrEmpty(title);
        var snapshot = Snapshot(items);
        var operation = new SyncOperation();
        var job = _jobs.Start(title, (progress, cancellationToken) =>
        {
            Run(operation, left, right, snapshot, progress, cancellationToken);
            return Task.CompletedTask;
        });
        operation.Attach(job);
        return operation;
    }

    private static void Run(
        SyncOperation operation,
        string left,
        string right,
        SyncItem[] items,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var total = SyncPlan.PendingCount(items);
        var done = 0;
        var report = DirectorySync.Apply(left, right, items, cancellationToken, rel =>
        {
            done++;
            progress.Report(new JobProgress(done, total, 0, 0, rel));
            return true;
        });
        operation.SetReport(report);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static SyncItem[] Snapshot(IReadOnlyList<SyncItem> items)
    {
        var copy = new SyncItem[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i] ?? throw new ArgumentException("An item is missing.", nameof(items));
            copy[i] = new SyncItem(item.Rel, item.IsDir, item.Left, item.Right, item.Status, item.Action);
        }

        return copy;
    }
}
