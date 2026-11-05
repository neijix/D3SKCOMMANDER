using D3skCommander.Core.Common;

namespace D3skCommander.Core.Splitter;

/// <summary>A size parsed from text such as <c>1.44M</c> or <c>512</c>.</summary>
public readonly struct ParsedSize
{
    private ParsedSize(ulong bytes, OperationError? error)
    {
        Bytes = bytes;
        Error = error;
    }

    public ulong Bytes { get; }

    public OperationError? Error { get; }

    public bool IsSuccess => Error is null;

    public static ParsedSize Ok(ulong bytes) => new(bytes, null);

    public static ParsedSize Fail(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ParsedSize(0, error);
    }
}
