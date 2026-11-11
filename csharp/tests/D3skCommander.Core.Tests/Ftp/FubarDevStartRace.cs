using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace D3skCommander.Core.Tests.Ftp;

/// <summary>
/// FubarDev.FtpServer 3.1.2 (the test FTP server, never shipped) has a start race:
/// PausableFtpService.StartAsync waits on a local SemaphoreSlim that a Progress callback releases
/// on the thread pool, and disposes it when the wait ends. When the callback runs after that
/// (seen on slow Windows CI runners), Release throws ObjectDisposedException on a thread-pool
/// thread: unhandled, it ends the test process and xUnit reports a catastrophic failure.
/// This handler drops only that exception (an ObjectDisposedException raised in that callback);
/// every other unhandled exception still ends the run.
/// </summary>
internal static class FubarDevStartRace
{
    [ModuleInitializer]
    internal static void Install()
    {
        try
        {
            ExceptionHandling.SetUnhandledExceptionHandler(IsStartRace);
        }
        catch (InvalidOperationException)
        {
            // A handler is already set (the test host's own): leave it in place.
        }
    }

    internal static bool IsStartRace(Exception exception) =>
        exception is ObjectDisposedException
        && exception.StackTrace is { } stack
        && stack.Contains("System.Threading.SemaphoreSlim.Release", StringComparison.Ordinal)
        && stack.Contains("FubarDev.FtpServer.Networking.PausableFtpService", StringComparison.Ordinal);
}
