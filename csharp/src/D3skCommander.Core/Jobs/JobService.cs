namespace D3skCommander.Core.Jobs;

/// <summary>Schedules background work, keeps its result, and publishes completion.</summary>
public sealed class JobService : IJobService
{
    private readonly object _gate = new();
    private readonly List<IJob> _jobs = [];
    private readonly Dictionary<long, JobDefinition> _definitions = [];
    private readonly List<Action<JobCompleted>> _subscribers = [];
    private long _nextId;

    public IReadOnlyList<IJob> Jobs
    {
        get
        {
            lock (_gate)
            {
                return _jobs.ToArray();
            }
        }
    }

    public IJob Start(
        string title,
        IReadOnlyList<JobItem> items,
        Func<JobItem, IJobProgress, CancellationToken, Task<ItemResult>> work)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(work);
        var copy = CopyItems(items);
        var id = Interlocked.Increment(ref _nextId);
        var job = new RunningJob(id, title, copy, work, Publish);
        lock (_gate)
        {
            _jobs.Add(job);
            _definitions.Add(id, new JobDefinition(title, copy, work));
        }

        job.Queue();
        return job;
    }

    public IJob Start(string title, Func<IJobProgress, CancellationToken, Task> work)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(work);
        return Start(title, [new JobItem(title)], (_, progress, cancellationToken) => RunOne(work, progress, cancellationToken));
    }

    public IJob Retry(IJob job, IReadOnlyList<string> itemNames)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(itemNames);
        if (job.IsRunning)
        {
            throw new InvalidOperationException("A job can be retried once it has finished.");
        }

        var definition = Find(job);
        return Start(definition.Title, SelectItems(definition.Items, itemNames), definition.Work);
    }

    public IDisposable Subscribe(Action<JobCompleted> onCompleted)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);
        lock (_gate)
        {
            _subscribers.Add(onCompleted);
        }

        return new Subscription(this, onCompleted);
    }

    private JobDefinition Find(IJob job)
    {
        lock (_gate)
        {
            if (_definitions.TryGetValue(job.Id, out var definition))
            {
                return definition;
            }
        }

        throw new ArgumentException("The job is not from this service.", nameof(job));
    }

    private void Publish(JobCompleted message)
    {
        Action<JobCompleted>[] handlers;
        lock (_gate)
        {
            handlers = _subscribers.ToArray();
        }

        foreach (var handler in handlers)
        {
            try
            {
                handler(message);
            }
            catch (Exception)
            {
                // A subscriber must not change the job result or skip the others.
            }
        }
    }

    private void Unsubscribe(Action<JobCompleted> handler)
    {
        lock (_gate)
        {
            _subscribers.Remove(handler);
        }
    }

    private static async Task<ItemResult> RunOne(
        Func<IJobProgress, CancellationToken, Task> work,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var task = work(progress, cancellationToken);
        if (task is null)
        {
            throw new InvalidOperationException("the work returned no task");
        }

        await task.ConfigureAwait(false);
        return ItemResult.Success;
    }

    private static JobItem[] CopyItems(IReadOnlyList<JobItem> items)
    {
        var copy = new JobItem[items.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                throw new ArgumentException("An item is missing.", nameof(items));
            }

            if (!names.Add(item.Name))
            {
                throw new ArgumentException("Two items share the same name.", nameof(items));
            }

            copy[i] = item;
        }

        return copy;
    }

    private static JobItem[] SelectItems(IReadOnlyList<JobItem> items, IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            throw new ArgumentException("Retry needs at least one item.", nameof(names));
        }

        var byName = new Dictionary<string, JobItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            byName[item.Name] = item;
        }

        var chosen = new JobItem[names.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            ArgumentException.ThrowIfNullOrEmpty(name);
            if (!seen.Add(name))
            {
                throw new ArgumentException("Retry lists an item twice.", nameof(names));
            }

            if (!byName.TryGetValue(name, out var item))
            {
                throw new ArgumentException("Retry lists an item this job did not run.", nameof(names));
            }

            chosen[i] = item;
        }

        return chosen;
    }

    private sealed class JobDefinition
    {
        public JobDefinition(
            string title,
            IReadOnlyList<JobItem> items,
            Func<JobItem, IJobProgress, CancellationToken, Task<ItemResult>> work)
        {
            Title = title;
            Items = items;
            Work = work;
        }

        public string Title { get; }

        public IReadOnlyList<JobItem> Items { get; }

        public Func<JobItem, IJobProgress, CancellationToken, Task<ItemResult>> Work { get; }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly JobService _service;
        private Action<JobCompleted>? _handler;

        public Subscription(JobService service, Action<JobCompleted> handler)
        {
            _service = service;
            _handler = handler;
        }

        public void Dispose()
        {
            var handler = Interlocked.Exchange(ref _handler, null);
            if (handler is not null)
            {
                _service.Unsubscribe(handler);
            }
        }
    }
}
