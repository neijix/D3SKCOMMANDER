namespace D3skCommander.Core.Jobs;

/// <summary>
/// Progress handed to the work. <see cref="Current"/> is the latest snapshot;
/// <see cref="IProgress{T}.Report"/> replaces it with absolute counts, not a delta.
/// </summary>
public interface IJobProgress : IProgress<JobProgress>
{
    JobProgress Current { get; }
}
