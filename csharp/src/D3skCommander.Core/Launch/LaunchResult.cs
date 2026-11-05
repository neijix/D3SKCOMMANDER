using D3skCommander.Core.Common;

namespace D3skCommander.Core.Launch;

/// <summary>How many processes started, or the typed error from the one that did not.</summary>
public sealed class LaunchResult
{
    private LaunchResult(int started, OperationError? error)
    {
        Started = started;
        Error = error;
    }

    public int Started { get; }

    public OperationError? Error { get; }

    public bool IsSuccess => Error is null;

    public static LaunchResult Ok(int started)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(started);
        return new LaunchResult(started, null);
    }

    public static LaunchResult Fail(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new LaunchResult(0, error);
    }
}
