using Avalonia.Threading;

namespace D3skCommander.App.Host;

/// <summary>
/// The Avalonia UI thread. Timers and dialogs from a background thread wait here; on the UI
/// thread itself the action runs at once.
/// </summary>
public static class UiThread
{
    public static bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    public static void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Invoke(action);
    }
}
