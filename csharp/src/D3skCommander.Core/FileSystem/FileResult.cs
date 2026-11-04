using D3skCommander.Core.Common;

namespace D3skCommander.Core.FileSystem;

/// <summary>A listing or path call. Failure is an <see cref="OperationError"/>, not a thrown exception.</summary>
public readonly struct FileResult<T>
    where T : class
{
    private readonly T? _value;
    private readonly OperationError? _error;

    private FileResult(T? value, OperationError? error)
    {
        _value = value;
        _error = error;
    }

    public OperationError? Error => _error;

    public bool IsSuccess => _error is null;

    public T Value
    {
        get
        {
            if (_error is not null || _value is null)
            {
                throw new InvalidOperationException(_error?.Message ?? "No value.");
            }

            return _value;
        }
    }

    public static FileResult<T> Ok(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new FileResult<T>(value, null);
    }

    public static FileResult<T> Fail(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new FileResult<T>(null, error);
    }
}

/// <summary>A call that only succeeds or fails, such as creating a file.</summary>
public readonly struct FileStatus
{
    private FileStatus(OperationError? error) => Error = error;

    public OperationError? Error { get; }

    public bool IsSuccess => Error is null;

    public static FileStatus Success => new(null);

    public static FileStatus Fail(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new FileStatus(error);
    }
}
