using System.Collections.Concurrent;

namespace D3skCommander.App.Host;

/// <summary>
/// The GLFW thread. Timers and dialogs from a background thread wait here.
/// When no thread is installed (unit tests), the caller is the UI thread.
/// </summary>
public static class UiThread
{
    private static readonly ConcurrentQueue<Action> Queue = new();
    private static Thread? _ui;

    public static void Install(Thread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        _ui = thread;
    }

    public static bool CheckAccess() => _ui is null || ReferenceEquals(_ui, Thread.CurrentThread);

    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            action();
            return;
        }

        Queue.Enqueue(action);
    }

    public static void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            action();
            return;
        }

        Exception? error = null;
        using var done = new ManualResetEventSlim(false);
        Queue.Enqueue(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    public static void Drain()
    {
        while (Queue.TryDequeue(out var action))
        {
            action();
        }
    }
}
