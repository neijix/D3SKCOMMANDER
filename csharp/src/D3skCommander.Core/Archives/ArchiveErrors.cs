using D3skCommander.Core.Common;
using SharpCompress.Common;

namespace D3skCommander.Core.Archives;

internal static class ArchiveErrors
{
    public static OperationError From(Exception exception, string? path)
    {
        if (exception is ArchiveException)
        {
            var message = string.IsNullOrEmpty(exception.Message) ? "invalid archive" : exception.Message;
            return OperationError.InvalidData(message, path);
        }

        return OperationError.FromException(exception, path);
    }

    public static bool IsIo(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    public static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (IsIo(ex))
        {
        }
    }
}
