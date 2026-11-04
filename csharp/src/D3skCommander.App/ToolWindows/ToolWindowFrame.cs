using D3skCommander.App.Shell;

namespace D3skCommander.App.ToolWindows;

/// <summary>Position and size in screen pixels. Width and height are at least 1 after <see cref="Clamp"/>.</summary>
public readonly record struct ToolWindowFrame(int X, int Y, int Width, int Height)
{
    public static ToolWindowFrame Clamp(ToolWindowFrame frame, ScreenRect screen)
    {
        var width = Fit(frame.Width, screen.Width);
        var height = Fit(frame.Height, screen.Height);
        var x = Slide(frame.X, screen.X, screen.Width, width);
        var y = Slide(frame.Y, screen.Y, screen.Height, height);
        return new ToolWindowFrame(x, y, width, height);
    }

    private static int Fit(int requested, int room)
    {
        var limit = room < 1 ? 1 : room;
        var size = requested < 1 ? limit : requested;
        return size < limit ? size : limit;
    }

    private static int Slide(int origin, int screenOrigin, int screenExtent, int size)
    {
        var end = screenOrigin + (screenExtent < size ? 0 : screenExtent - size);
        if (origin < screenOrigin)
        {
            return screenOrigin;
        }

        if (origin > end)
        {
            return end;
        }

        return origin;
    }
}
