namespace D3skCommander.Core.Jobs;

/// <summary>
/// Completion message. Subscribers run on the worker thread;
/// Core does not move them onto a UI thread.
/// </summary>
public sealed class JobCompleted
{
    public JobCompleted(IJob job, JobResult result)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(result);
        Job = job;
        Result = result;
    }

    public IJob Job { get; }

    public JobResult Result { get; }
}
