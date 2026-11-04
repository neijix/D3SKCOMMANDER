namespace D3skCommander.Core.Jobs;

/// <summary>The one entry point for background work.</summary>
public interface IJobService
{
    IReadOnlyList<IJob> Jobs { get; }

    /// <summary>Queues item work and returns before it runs. Names must be unique; they are the retry keys.</summary>
    IJob Start(
        string title,
        IReadOnlyList<JobItem> items,
        Func<JobItem, IJobProgress, CancellationToken, Task<ItemResult>> work);

    /// <summary>Queues one task and returns before it runs. A thrown failure is one failed item named <paramref name="title"/>.</summary>
    IJob Start(string title, Func<IJobProgress, CancellationToken, Task> work);

    /// <summary>A new job that runs only <paramref name="itemNames"/>, in that order, with the same work.</summary>
    IJob Retry(IJob job, IReadOnlyList<string> itemNames);

    /// <summary>
    /// Registers <paramref name="onCompleted"/>. The handler runs on the worker thread
    /// before <see cref="IJob.Completion"/> is signalled, so it must not wait on that task.
    /// </summary>
    IDisposable Subscribe(Action<JobCompleted> onCompleted);
}
