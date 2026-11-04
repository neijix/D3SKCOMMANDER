using D3skCommander.Core.Common;

namespace D3skCommander.Core.Jobs;

/// <summary>What one item did. A thrown exception is recorded separately, via <c>OperationError.FromException</c>.</summary>
public sealed class ItemResult
{
    private ItemResult(OperationError? error, bool retryable)
    {
        Error = error;
        Retryable = retryable;
    }

    public static ItemResult Success { get; } = new(null, false);

    public OperationError? Error { get; }

    public bool Retryable { get; }

    public static ItemResult Failed(OperationError error, bool retryable = true)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ItemResult(error, retryable);
    }
}
