using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.FileOps;

/// <summary>Mutable totals for one start. A retry resets them on its first item.</summary>
internal sealed class FileOpSession
{
    private readonly List<string> _messages = [];
    private readonly List<ConflictQuestion> _conflicts = [];
    private DateTime _lastReport = DateTime.MinValue;

    public int TotalFiles { get; private set; }

    public long TotalBytes { get; private set; }

    public int HandledFiles { get; private set; }

    public int CompletedFiles { get; private set; }

    public long DoneBytes { get; set; }

    public int ErrorCount { get; private set; }

    public bool Cancelled { get; private set; }

    public bool DestinationReady { get; set; }

    public OperationError? DestinationError { get; set; }

    public OperationError? FirstError { get; private set; }

    public OperationError? LastError { get; private set; }

    public string CurrentPath { get; set; } = "";

    public void Reset()
    {
        TotalFiles = 0;
        TotalBytes = 0;
        HandledFiles = 0;
        CompletedFiles = 0;
        DoneBytes = 0;
        ErrorCount = 0;
        Cancelled = false;
        DestinationReady = false;
        DestinationError = null;
        FirstError = null;
        LastError = null;
        CurrentPath = "";
        _lastReport = DateTime.MinValue;
        _messages.Clear();
        _conflicts.Clear();
    }

    public void MarkCancelled() => Cancelled = true;

    public void AddTotals(int files, long bytes)
    {
        TotalFiles += files;
        TotalBytes += bytes;
    }

    public void Complete(int files, long bytes)
    {
        CompletedFiles += files;
        HandledFiles += files;
        DoneBytes += bytes;
    }

    public void Skip(int files, long bytes)
    {
        HandledFiles += files;
        DoneBytes += bytes;
    }

    public void AddError(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ErrorCount++;
        LastError = error;
        if (FirstError is null)
        {
            FirstError = error;
        }

        _messages.Add(error.Message);
    }

    public void AddConflict(ConflictQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        _conflicts.Add(question);
    }

    public IReadOnlyList<string> CopyMessages()
    {
        var copy = new string[_messages.Count];
        for (var i = 0; i < _messages.Count; i++)
        {
            copy[i] = _messages[i];
        }

        return copy;
    }

    public IReadOnlyList<ConflictQuestion> CopyConflicts()
    {
        var copy = new ConflictQuestion[_conflicts.Count];
        for (var i = 0; i < _conflicts.Count; i++)
        {
            copy[i] = _conflicts[i];
        }

        return copy;
    }

    public void Report(IJobProgress progress, Action<FileOpProgress>? onProgress, bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && _lastReport != DateTime.MinValue && now - _lastReport < TimeSpan.FromMilliseconds(50))
        {
            return;
        }

        _lastReport = now;
        var snapshot = new FileOpProgress(CurrentPath, HandledFiles, TotalFiles, DoneBytes, TotalBytes);
        progress.Report(new JobProgress(
            snapshot.DoneFiles,
            snapshot.TotalFiles,
            snapshot.DoneBytes,
            snapshot.TotalBytes,
            snapshot.CurrentPath));
        if (onProgress is not null)
        {
            onProgress(snapshot);
        }
    }
}
