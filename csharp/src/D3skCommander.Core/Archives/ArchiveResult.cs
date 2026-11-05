using D3skCommander.Core.Common;

namespace D3skCommander.Core.Archives;

/// <summary>A list, extract or pack call. Failure is an <see cref="OperationError"/>, not a thrown exception.</summary>
public readonly struct ArchiveResult<T>
{
    private readonly T? _value;
    private readonly bool _ok;

    private ArchiveResult(T? value, bool ok, OperationError? error)
    {
        _value = value;
        _ok = ok;
        Error = error;
    }

    public OperationError? Error { get; }

    public bool IsSuccess => _ok;

    public T Value
    {
        get
        {
            if (!_ok)
            {
                throw new InvalidOperationException(Error?.Message ?? "No value.");
            }

            return _value!;
        }
    }

    public static ArchiveResult<T> Ok(T value) => new(value, true, null);

    public static ArchiveResult<T> Fail(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ArchiveResult<T>(default, false, error);
    }
}
