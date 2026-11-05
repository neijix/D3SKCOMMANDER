using D3skCommander.Core.Common;

namespace D3skCommander.Core.Sync;

internal static class SyncIo
{
    public static bool IsFileSystem(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;

    /// <summary>v0.1's <c>path: error</c> text, with the kind taken from the exception.</summary>
    public static OperationError At(string path, Exception exception)
    {
        var typed = OperationError.FromException(exception, path);
        return new OperationError(typed.Kind, path + ": " + typed.Message, path, typed.OsError);
    }
}
