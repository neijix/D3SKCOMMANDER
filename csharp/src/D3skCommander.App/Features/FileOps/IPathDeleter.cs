namespace D3skCommander.App.Features.FileOps;

/// <summary>
/// Deletes paths a tool window names (disk usage) the way Delete deletes the panel selection: the guard, the
/// preferences (trash, confirmation) and a job. Never throws for a file error: the job reports it.
/// </summary>
public interface IPathDeleter
{
    /// <summary>True when the delete ran (confirmed, not refused, not busy) — the paths may still have failed one by one.</summary>
    Task<bool> DeletePathsAsync(IReadOnlyList<string> paths);
}
