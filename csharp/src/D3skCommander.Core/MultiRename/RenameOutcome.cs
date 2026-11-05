namespace D3skCommander.Core.MultiRename;

/// <summary>Pairs actually renamed, or the v0.1 error text when nothing was left changed.</summary>
public sealed class RenameOutcome
{
    private RenameOutcome(IReadOnlyList<RenamePair> done, string? error)
    {
        Done = done;
        Error = error;
    }

    public IReadOnlyList<RenamePair> Done { get; }

    public string? Error { get; }

    public bool IsSuccess => Error is null;

    public static RenameOutcome Ok(IReadOnlyList<RenamePair> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        return new RenameOutcome(done, null);
    }

    public static RenameOutcome Fail(string error)
    {
        ArgumentException.ThrowIfNullOrEmpty(error);
        return new RenameOutcome([], error);
    }
}
