using D3skCommander.Core.Common;

namespace D3skCommander.Core.Timestamps;

/// <summary>One file after AddTime, ReDate or touch, with a typed error when that file failed.</summary>
public readonly record struct FileTimeResult(string Path, OperationError? Error)
{
    public bool IsSuccess => Error is null;

    public static FileTimeResult Ok(string path) => new(path, null);

    public static FileTimeResult Fail(string path, string message) =>
        new(path, OperationError.InvalidData(message, path));

    public static FileTimeResult FromException(string path, Exception exception) =>
        new(path, OperationError.FromException(exception, path));
}
