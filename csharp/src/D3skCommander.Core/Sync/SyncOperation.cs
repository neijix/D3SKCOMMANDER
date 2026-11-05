using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Sync;

/// <summary>A sync plan queued on the job service. Read <see cref="Report"/> after <see cref="Job"/> completes.</summary>
public sealed class SyncOperation
{
    private readonly object _gate = new();
    private IJob? _job;
    private SyncReport _report = new();

    internal SyncOperation()
    {
    }

    public IJob Job => _job ?? throw new InvalidOperationException("The sync job is not started.");

    public SyncReport Report
    {
        get
        {
            lock (_gate)
            {
                return _report;
            }
        }
    }

    internal void Attach(IJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        _job = job;
    }

    internal void SetReport(SyncReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (_gate)
        {
            _report = report;
        }
    }
}
