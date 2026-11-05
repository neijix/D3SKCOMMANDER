using D3skCommander.Core.Common;

namespace D3skCommander.Core.Tools;

/// <summary>A local date parsed for the time tools, or the reason the text was rejected.</summary>
public readonly record struct DateParseResult(DateTimeOffset Time, OperationError? Error)
{
    public bool IsSuccess => Error is null;

    public static DateParseResult Ok(DateTimeOffset time) => new(time, null);

    public static DateParseResult Fail(string message) => new(default, OperationError.InvalidData(message));
}
