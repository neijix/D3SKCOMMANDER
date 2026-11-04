using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.FileOps;

/// <summary>A file operation queued on the job service, plus the counts v0.1 reported at the end.</summary>
public sealed class FileOperation
{
    private readonly FileOpSession _session;

    internal FileOperation(IJob job, FileOpSession session)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(session);
        Job = job;
        _session = session;
    }

    public IJob Job { get; }

    /// <summary>Files actually copied, moved, or deleted. Skips and failures are not included.</summary>
    public int CompletedFiles => _session.CompletedFiles;

    public int ErrorCount => _session.ErrorCount;

    public bool WasCancelled => _session.Cancelled;

    public IReadOnlyList<string> ErrorMessages() => _session.CopyMessages();

    public IReadOnlyList<ConflictQuestion> Conflicts() => _session.CopyConflicts();
}
