using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using Xunit;

namespace D3skCommander.Core.Tests.Jobs;

/// <summary>
/// P40 and A2: background work runs through the job service, with progress,
/// cancellation and a typed result, off the calling thread. U5: Retry re-runs only the chosen items.
/// </summary>
public class JobServiceTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StartReturnsWhileTheJobIsStillRunning()
    {
        var service = new JobService();
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var caller = Environment.CurrentManagedThreadId;
        var worker = 0;
        var job = service.Start(
            "Copy",
            [new JobItem("a.txt")],
            (item, progress, cancellationToken) =>
            {
                worker = Environment.CurrentManagedThreadId;
                entered.Set();
                release.Wait();
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(ItemResult.Success);
            });

        try
        {
            Assert.False(job.Completion.IsCompleted);
            Assert.True(job.IsRunning);
            Assert.Contains(job, service.Jobs);
            Assert.True(entered.Wait(Bound, TestContext.Current.CancellationToken));
            Assert.NotEqual(caller, worker);
            Assert.False(job.Completion.IsCompleted);
            release.Set();
            var result = await Finish(job);
            Assert.Equal(JobOutcome.Completed, result.Outcome);
            Assert.Empty(result.Failed);
            Assert.False(job.IsRunning);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task ProgressReportsItemsBytesAndTheCurrentName()
    {
        var service = new JobService();
        using var gate = new Gate();
        var seen = new List<JobProgress>();
        var job = service.Start(
            "Copy",
            [new JobItem("a.txt", 40), new JobItem("b.txt", 60)],
            async (item, progress, cancellationToken) =>
            {
                if (item.Name == "a.txt")
                {
                    gate.Enter();
                    await gate.WaitForRelease();
                    var current = progress.Current;
                    progress.Report(current with { BytesDone = 15 });
                }

                return ItemResult.Success;
            });

        await gate.WaitUntilEntered();
        var baseline = job.Progress;
        job.ProgressChanged += seen.Add;
        gate.Release();
        var result = await Finish(job);

        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Equal(new JobProgress(0, 2, 0, 100, "a.txt"), baseline);
        Assert.Contains(new JobProgress(0, 2, 15, 100, "a.txt"), seen);
        Assert.Equal(new JobProgress(2, 2, 100, 100, "b.txt"), job.Progress);
    }

    [Fact]
    public async Task WorkerProgressIsKeptWhenTheJobHasNoDeclaredSize()
    {
        var service = new JobService();
        var job = service.Start("Scan", (progress, cancellationToken) =>
        {
            progress.Report(new JobProgress(2, 4, 10, 40, "folder"));
            return Task.CompletedTask;
        });

        var result = await Finish(job);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Equal(new JobProgress(2, 4, 10, 40, "folder"), job.Progress);
    }

    [Fact]
    public async Task CancelGivesACancelledResult()
    {
        var service = new JobService();
        using var gate = new Gate();
        var ran = new List<string>();
        JobCompleted? message = null;
        service.Subscribe(completed => message = completed);
        var job = service.Start(
            "Copy",
            [new JobItem("a.txt"), new JobItem("b.txt")],
            async (item, progress, cancellationToken) =>
            {
                ran.Add(item.Name);
                if (item.Name == "a.txt")
                {
                    gate.Enter();
                    await gate.WaitForRelease();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return ItemResult.Success;
            });

        await gate.WaitUntilEntered();
        job.Cancel();
        gate.Release();
        var result = await Finish(job);

        Assert.Equal(JobOutcome.Cancelled, result.Outcome);
        Assert.Empty(result.Failed);
        Assert.Equal(["a.txt"], ran);
        Assert.NotNull(message);
        Assert.Same(result, message.Result);
    }

    [Fact]
    public async Task AThrownFailureBecomesATypedError()
    {
        var service = new JobService();
        var job = service.Start(
            "Copy",
            [new JobItem("secret.txt"), new JobItem("ok.txt")],
            (item, progress, cancellationToken) =>
            {
                if (item.Name == "secret.txt")
                {
                    throw new UnauthorizedAccessException("access denied");
                }

                return Task.FromResult(ItemResult.Success);
            });

        var result = await Finish(job);
        var failed = Assert.Single(result.Failed);
        var expected = OperationError.FromException(new UnauthorizedAccessException("access denied"), "secret.txt");
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Equal("secret.txt", failed.Item);
        Assert.Equal(expected, failed.Error);
        Assert.True(failed.Retryable);
    }

    [Fact]
    public async Task AThrownFailureInBackgroundWorkBecomesATypedError()
    {
        var service = new JobService();
        var job = service.Start(
            "Pack",
            (progress, cancellationToken) => Task.FromException(new InvalidDataException("corrupt")));

        var result = await Finish(job);
        var failed = Assert.Single(result.Failed);
        var expected = OperationError.FromException(new InvalidDataException("corrupt"), "Pack");
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Equal("Pack", failed.Item);
        Assert.Equal(expected, failed.Error);
        Assert.True(failed.Retryable);
    }

    [Fact]
    public async Task TheResultListsEachFailedItemWithItsTypedError()
    {
        var service = new JobService();
        var job = service.Start(
            "Copy",
            [new JobItem("a.txt"), new JobItem("b.txt"), new JobItem("c.txt")],
            (item, progress, cancellationToken) =>
            {
                if (item.Name == "b.txt")
                {
                    throw new UnauthorizedAccessException("access denied");
                }

                if (item.Name == "c.txt")
                {
                    return Task.FromResult(ItemResult.Failed(OperationError.NotFound("c.txt"), retryable: false));
                }

                return Task.FromResult(ItemResult.Success);
            });

        var result = await Finish(job);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.Failed.Count);
        Assert.Equal("b.txt", result.Failed[0].Item);
        Assert.Equal(ErrorKind.AccessDenied, result.Failed[0].Error.Kind);
        Assert.Equal("b.txt", result.Failed[0].Error.Path);
        Assert.True(result.Failed[0].Retryable);
        Assert.Equal("c.txt", result.Failed[1].Item);
        Assert.Equal(ErrorKind.NotFound, result.Failed[1].Error.Kind);
        Assert.Equal("c.txt", result.Failed[1].Error.Path);
        Assert.False(result.Failed[1].Retryable);
    }

    [Fact]
    public async Task RetryRunsOnlyTheChosenItems()
    {
        var service = new JobService();
        var ran = new List<string>();
        var job = service.Start(
            "Copy",
            [new JobItem("a.txt"), new JobItem("b.txt"), new JobItem("c.txt")],
            (item, progress, cancellationToken) =>
            {
                ran.Add(item.Name);
                if (item.Name == "b.txt" || item.Name == "c.txt")
                {
                    return Task.FromResult(ItemResult.Failed(OperationError.Network("download failed", item.Name), true));
                }

                return Task.FromResult(ItemResult.Success);
            });

        var first = await Finish(job);
        var retry = service.Retry(job, ["b.txt"]);
        var second = await Finish(retry);

        Assert.NotEqual(job.Id, retry.Id);
        Assert.Equal(["b.txt", "c.txt"], Names(first));
        Assert.Equal(["a.txt", "b.txt", "c.txt", "b.txt"], ran);
        Assert.Equal(["b.txt"], Names(second));
        Assert.Equal(ErrorKind.Network, second.Failed[0].Error.Kind);
    }

    [Fact]
    public async Task RetryRejectsItemsTheJobDidNotRun()
    {
        var service = new JobService();
        var job = service.Start("Copy", [new JobItem("a.txt")], (_, _, _) => Task.FromResult(ItemResult.Success));
        await Finish(job);

        Assert.Throws<ArgumentException>(() => service.Retry(job, ["missing.txt"]));
        Assert.Throws<ArgumentException>(() => service.Retry(job, []));
        Assert.Throws<ArgumentException>(() => service.Retry(job, ["a.txt", "a.txt"]));
        Assert.Throws<ArgumentException>(() => new JobService().Retry(job, ["a.txt"]));
    }

    [Fact]
    public async Task RetryRejectsAJobThatIsStillRunning()
    {
        var service = new JobService();
        using var gate = new Gate();
        var job = service.Start(
            "Copy",
            [new JobItem("a.txt")],
            async (item, progress, cancellationToken) =>
            {
                gate.Enter();
                await gate.WaitForRelease();
                return ItemResult.Success;
            });

        await gate.WaitUntilEntered();
        Assert.Throws<InvalidOperationException>(() => service.Retry(job, ["a.txt"]));
        gate.Release();
        var result = await Finish(job);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
    }

    [Fact]
    public void StartRejectsDuplicateItemNames()
    {
        var service = new JobService();
        var items = new[] { new JobItem("a.txt"), new JobItem("a.txt") };
        Assert.Throws<ArgumentException>(() =>
            service.Start("Copy", items, (_, _, _) => Task.FromResult(ItemResult.Success)));
    }

    [Fact]
    public async Task CompletionIsAMessageSubscribersReceive()
    {
        var service = new JobService();
        var received = new List<JobCompleted>();
        using var failing = service.Subscribe(_ => throw new InvalidOperationException("subscriber failed"));
        using var ok = service.Subscribe(received.Add);
        var job = service.Start("Copy", [new JobItem("a.txt")], (_, _, _) => Task.FromResult(ItemResult.Success));
        var result = await Finish(job);
        var message = Assert.Single(received);
        Assert.Same(job, message.Job);
        Assert.Same(result, message.Result);
        Assert.Equal("Copy", message.Job.Title);
        Assert.Equal(JobOutcome.Completed, message.Result.Outcome);

        failing.Dispose();
        ok.Dispose();
        received.Clear();
        var again = service.Start("Copy", [new JobItem("b.txt")], (_, _, _) => Task.FromResult(ItemResult.Success));
        await Finish(again);
        Assert.Empty(received);
    }

    private static Task<JobResult> Finish(IJob job) => job.Completion.WaitAsync(Bound);

    private static List<string> Names(JobResult result)
    {
        var names = new List<string>(result.Failed.Count);
        foreach (var failed in result.Failed)
        {
            names.Add(failed.Item);
        }

        return names;
    }

    private sealed class Gate : IDisposable
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Enter() => _entered.TrySetResult();

        public Task WaitUntilEntered() => _entered.Task.WaitAsync(Bound);

        public Task WaitForRelease() => _release.Task;

        public void Release() => _release.TrySetResult();

        public void Dispose() => _release.TrySetResult();
    }
}
