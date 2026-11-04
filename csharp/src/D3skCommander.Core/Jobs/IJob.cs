namespace D3skCommander.Core.Jobs;

/// <summary>One background job. <see cref="Completion"/> finishes off the thread that started it.</summary>
public interface IJob
{
    long Id { get; }

    string Title { get; }

    JobProgress Progress { get; }

    bool IsRunning { get; }

    Task<JobResult> Completion { get; }

    /// <summary>Raised for each progress report, on the worker thread.</summary>
    event Action<JobProgress> ProgressChanged;

    void Cancel();
}
