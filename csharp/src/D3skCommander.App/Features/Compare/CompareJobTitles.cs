using D3skCommander.Core.Jobs;

namespace D3skCommander.App.Features.Compare;

/// <summary>
/// The compare session names its background job with a fixed English word.
/// This relay is what the session receives, and it stores the translated tool title instead.
/// </summary>
public sealed class CompareJobTitles : IJobService
{
    private readonly IJobService _inner;
    private readonly string _title;

    public CompareJobTitles(IJobService inner, string title)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrEmpty(title);
        _inner = inner;
        _title = title;
    }

    public IReadOnlyList<IJob> Jobs => _inner.Jobs;

    public IJob Start(
        string title,
        IReadOnlyList<JobItem> items,
        Func<JobItem, IJobProgress, CancellationToken, Task<ItemResult>> work)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        return _inner.Start(title, items, work);
    }

    public IJob Start(string title, Func<IJobProgress, CancellationToken, Task> work)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(work);
        return _inner.Start(_title, work);
    }

    public IJob Retry(IJob job, IReadOnlyList<string> itemNames) => _inner.Retry(job, itemNames);

    public IDisposable Subscribe(Action<JobCompleted> onCompleted) => _inner.Subscribe(onCompleted);
}
