namespace D3skCommander.Core.Update;

/// <summary>One updater step. Failure keeps v0.1's English detail in <see cref="Error"/>.</summary>
public sealed class UpdateStep<T>
    where T : class
{
    private UpdateStep(T? value, string? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public string? Error { get; }

    public bool IsSuccess => Error is null;

    public static UpdateStep<T> Ok(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new UpdateStep<T>(value, null);
    }

    public static UpdateStep<T> Fail(string error)
    {
        ArgumentException.ThrowIfNullOrEmpty(error);
        return new UpdateStep<T>(default, error);
    }
}
