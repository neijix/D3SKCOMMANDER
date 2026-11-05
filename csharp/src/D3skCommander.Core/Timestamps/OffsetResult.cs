using D3skCommander.Core.Common;

namespace D3skCommander.Core.Timestamps;

/// <summary>A parsed AddTime offset in seconds, or the reason the text was rejected.</summary>
public readonly record struct OffsetResult(long Seconds, OperationError? Error)
{
    public bool IsSuccess => Error is null;

    public static OffsetResult Ok(long seconds) => new(seconds, null);

    public static OffsetResult Fail(string message) => new(0, OperationError.InvalidData(message));
}
