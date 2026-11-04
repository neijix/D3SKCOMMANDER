using D3skCommander.Core.Common;

namespace D3skCommander.Core.Jobs;

/// <summary>Runs one job off the caller, on the default task scheduler.</summary>
internal sealed class RunningJob : IJob
{
    private readonly string _title;
    private readonly IReadOnlyList<JobItem> _items;
    private readonly Func<JobItem, IJobProgress, CancellationToken, Task<ItemResult>> _work;
    private readonly Action<JobCompleted> _onCompleted;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ProgressReporter _progress = new();
    private readonly TaskCompletionSource<JobResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly long _bytesTotal;
    private bool _finished;

    public RunningJob(
        long id,
        string title,
        IReadOnlyList<JobItem> items,
        Func<JobItem, IJobProgress, CancellationToken, Task<ItemResult>> work,
        Action<JobCompleted> onCompleted)
    {
        Id = id;
        _title = title;
        _items = items;
        _work = work;
        _onCompleted = onCompleted;
        _bytesTotal = Sum(items);
    }

    public long Id { get; }

    public string Title => _title;

    public JobProgress Progress => _progress.Current;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return !_finished;
            }
        }
    }

    public Task<JobResult> Completion => _done.Task;

    public event Action<JobProgress> ProgressChanged
    {
        add => _progress.Changed += value;
        remove => _progress.Changed -= value;
    }

    public void Cancel()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The token source is released once the job has finished.
        }
    }

    internal void Queue()
    {
        // Task.Run uses the default scheduler, so the caller does not run the job.
        _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        var failed = new List<ItemReport>();
        var outcome = JobOutcome.Completed;
        try
        {
            outcome = await ExecuteAsync(failed).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            outcome = JobOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            failed.Add(Failure(_title, ex));
        }

        Finish(outcome, failed);
    }

    private async Task<JobOutcome> ExecuteAsync(List<ItemReport> failed)
    {
        long bytesDone = 0;
        for (var index = 0; index < _items.Count; index++)
        {
            if (_cancellation.IsCancellationRequested)
            {
                return JobOutcome.Cancelled;
            }

            var item = _items[index];
            PublishBoundary(index, item.Name, bytesDone);
            if (_cancellation.IsCancellationRequested)
            {
                return JobOutcome.Cancelled;
            }

            var before = _progress.Current;
            var step = await RunItemAsync(item).ConfigureAwait(false);
            if (step.Failure is not null)
            {
                failed.Add(step.Failure);
            }

            if (step.Cancelled)
            {
                return JobOutcome.Cancelled;
            }

            bytesDone = Advance(bytesDone, item);
            PublishAfterItem(index + 1, item.Name, bytesDone, before);
        }

        return JobOutcome.Completed;
    }

    private async Task<Step> RunItemAsync(JobItem item)
    {
        try
        {
            var task = _work(item, _progress, _cancellation.Token);
            if (task is null)
            {
                return new Step(MissingResult(item.Name), false);
            }

            var result = await task.ConfigureAwait(false);
            var failure = AsReport(item.Name, result);
            return new Step(failure, _cancellation.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            return new Step(null, true);
        }
        catch (Exception ex)
        {
            return new Step(Failure(item.Name, ex), false);
        }
    }

    private void PublishBoundary(int itemsDone, string currentName, long bytesDone)
    {
        var total = _bytesTotal > 0 ? _bytesTotal : _progress.Current.BytesTotal;
        _progress.Report(new JobProgress(itemsDone, _items.Count, bytesDone, total, currentName));
    }

    private void PublishAfterItem(int itemsDone, string itemName, long bytesDone, JobProgress before)
    {
        var current = _progress.Current;
        var reported = !current.Equals(before);
        var done = _bytesTotal > 0 ? bytesDone : current.BytesDone;
        var total = _bytesTotal > 0 ? _bytesTotal : Math.Max(current.BytesTotal, done);
        var itemsTotal = Math.Max(_items.Count, current.ItemsTotal);
        var shownDone = reported ? Math.Max(itemsDone, current.ItemsDone) : itemsDone;
        var name = reported ? current.CurrentName : itemName;
        _progress.Report(new JobProgress(shownDone, itemsTotal, done, total, name));
    }

    private long Advance(long bytesDone, JobItem item)
    {
        if (item.Bytes > 0)
        {
            return bytesDone + item.Bytes;
        }

        return Math.Max(bytesDone, _progress.Current.BytesDone);
    }

    private void Finish(JobOutcome outcome, List<ItemReport> failed)
    {
        JobResult result;
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            result = new JobResult(outcome, failed);
        }

        _onCompleted(new JobCompleted(this, result));
        _done.TrySetResult(result);
        _cancellation.Dispose();
    }

    private static ItemReport Failure(string item, Exception exception) =>
        new(item, OperationError.FromException(exception, item), retryable: true);

    private static ItemReport MissingResult(string item) =>
        new(item, OperationError.Other("the item work returned no result", item), retryable: true);

    private static ItemReport? AsReport(string item, ItemResult? result)
    {
        if (result is null)
        {
            return MissingResult(item);
        }

        if (result.Error is null)
        {
            return null;
        }

        return new ItemReport(item, result.Error, result.Retryable);
    }

    private static long Sum(IReadOnlyList<JobItem> items)
    {
        long total = 0;
        foreach (var item in items)
        {
            total += item.Bytes;
        }

        return total;
    }

    private readonly struct Step
    {
        public Step(ItemReport? failure, bool cancelled)
        {
            Failure = failure;
            Cancelled = cancelled;
        }

        public ItemReport? Failure { get; }

        public bool Cancelled { get; }
    }

    private sealed class ProgressReporter : IJobProgress
    {
        private readonly object _gate = new();
        private JobProgress _current = JobProgress.Empty;
        private Action<JobProgress>? _changed;

        public event Action<JobProgress> Changed
        {
            add
            {
                lock (_gate)
                {
                    _changed += value;
                }
            }

            remove
            {
                lock (_gate)
                {
                    _changed -= value;
                }
            }
        }

        public JobProgress Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        public void Report(JobProgress value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value.ItemsDone);
            ArgumentOutOfRangeException.ThrowIfNegative(value.ItemsTotal);
            ArgumentOutOfRangeException.ThrowIfNegative(value.BytesDone);
            ArgumentOutOfRangeException.ThrowIfNegative(value.BytesTotal);
            ArgumentNullException.ThrowIfNull(value.CurrentName);

            Action<JobProgress>? handlers;
            lock (_gate)
            {
                _current = value;
                handlers = _changed;
            }

            Raise(handlers, value);
        }

        private static void Raise(Action<JobProgress>? handlers, JobProgress value)
        {
            if (handlers is null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<JobProgress>)handler).Invoke(value);
                }
                catch (Exception)
                {
                    // Progress is informational. One subscriber must not fail the item.
                }
            }
        }
    }
}
