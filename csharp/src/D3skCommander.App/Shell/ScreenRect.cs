namespace D3skCommander.App.Shell;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

/// <summary>A point in screen pixels.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>The monitor the main window sits on.</summary>
public readonly record struct DesktopScreen(ScreenRect WorkingArea, double Scaling)
{
    /// <summary>The fallback when the platform reports no screen.</summary>
    public static DesktopScreen Fallback { get; } = new(new ScreenRect(0, 0, 1920, 1080), 1);

    /// <summary>The working area (pixels) and scaling Avalonia reports; the fallback for <c>null</c>.</summary>
    public static DesktopScreen From(Avalonia.Platform.Screen? screen)
    {
        if (screen is null)
        {
            return Fallback;
        }

        var area = screen.WorkingArea;
        return new DesktopScreen(new ScreenRect(area.X, area.Y, area.Width, area.Height), screen.Scaling);
    }
}
