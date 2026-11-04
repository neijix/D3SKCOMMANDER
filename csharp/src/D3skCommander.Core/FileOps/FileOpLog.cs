using D3skCommander.Core.OperationLog;

namespace D3skCommander.Core.FileOps;

/// <summary>One line per finished file when the log is enabled. A failed append never fails the operation.</summary>
internal static class FileOpLog
{
    public static void Write(IOperationLog log, string operation, string source, string? destination, string? error)
    {
        try
        {
            log.Record(operation, source, destination, error);
        }
        catch (Exception)
        {
            // v0.1 log_line: logging is best effort.
        }
    }
}
