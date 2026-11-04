using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using D3skCommander.App.Shell;

namespace D3skCommander.App.ToolWindows;

/// <summary>
/// Finds the open main window without taking it as a constructor dependency.
/// The shell builds that window from the modules, so injecting it would cycle.
/// Headless tests have no desktop lifetime; <see cref="Window.WindowOpenedEvent"/> sees them.
/// </summary>
internal static class ToolWindowOwners
{
    private static readonly List<WeakReference<MainWindow>> Open = [];
    private static bool _listening;

    public static void Listen()
    {
        if (_listening || Application.Current is null)
        {
            return;
        }

        _listening = true;
        var routes = RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel;
        Window.WindowOpenedEvent.AddClassHandler<Window>(OnOpened, routes, true);
        Window.WindowClosedEvent.AddClassHandler<Window>(OnClosed, routes, true);
    }

    public static Window Require()
    {
        var main = LifetimeOwner();
        if (main is not null)
        {
            return main;
        }

        var seen = LatestVisible();
        if (seen is not null)
        {
            return seen;
        }

        throw new InvalidOperationException("A tool window opens only while the main window is open.");
    }

    private static Window? LifetimeOwner()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is Window window)
        {
            return window;
        }

        return null;
    }

    private static void OnOpened(Window window, RoutedEventArgs e)
    {
        if (window is not MainWindow main)
        {
            return;
        }

        Open.Add(new WeakReference<MainWindow>(main));
    }

    private static void OnClosed(Window window, RoutedEventArgs e)
    {
        if (window is not MainWindow)
        {
            return;
        }

        for (var i = Open.Count - 1; i >= 0; i--)
        {
            if (!Open[i].TryGetTarget(out var main) || ReferenceEquals(main, window))
            {
                Open.RemoveAt(i);
            }
        }
    }

    private static MainWindow? LatestVisible()
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
