namespace D3skCommander.App.Shell;

/// <summary>
/// The open main windows. The shell builds the window from the modules, so tools and dialogs
/// find it here instead of taking it as a constructor dependency.
/// </summary>
internal static class ToolWindowOwners
{
    private static readonly List<WeakReference<MainWindow>> Open = [];

    public static void Listen()
    {
    }

    public static void Opened(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Open.Add(new WeakReference<MainWindow>(window));
    }

    public static void Closed(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        for (var i = Open.Count - 1; i >= 0; i--)
        {
            if (!Open[i].TryGetTarget(out var main) || ReferenceEquals(main, window))
            {
                Open.RemoveAt(i);
            }
        }
    }

    public static MainWindow Require() =>
        LatestVisible() ?? throw new InvalidOperationException("A tool window opens only while the main window is open.");

    public static MainWindow? LatestVisible()
    {
        MainWindow? found = null;
        for (var i = Open.Count - 1; i >= 0; i--)
        {
            if (!Open[i].TryGetTarget(out var window))
            {
                Open.RemoveAt(i);
                continue;
            }

            if (window.IsVisible && found is null)
            {
                found = window;
            }
        }

        return found;
    }
}
