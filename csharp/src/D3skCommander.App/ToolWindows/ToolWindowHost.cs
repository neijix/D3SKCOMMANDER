using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.ToolWindows;

/// <summary>One owned top-level window per kind. Geometry stays in <see cref="IToolWindowGeometry"/>.</summary>
public sealed class ToolWindowHost : IToolWindowHost
{
    private readonly ILocalizer _localizer;
    private readonly IToolWindowGeometry _geometry;
    private readonly Dictionary<string, Func<IShellWidget>> _views = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolWindow> _open = new(StringComparer.Ordinal);

    public ToolWindowHost(ILocalizer localizer, IToolWindowGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(geometry);
        _localizer = localizer;
        _geometry = geometry;
    }

    public void Register(ToolKind kind, Func<IShellWidget> content)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(content);
        _views[kind.Id] = content;
    }

    public bool IsOpen(ToolKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return _open.TryGetValue(kind.Id, out var window) && window.IsVisible;
    }

    public ToolWindow Open(ToolKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (_open.TryGetValue(kind.Id, out var existing) && existing.IsVisible)
        {
            existing.Activate();
            return existing;
        }

        _open.Remove(kind.Id);
        return ShowNew(kind, ToolWindowOwners.Require());
    }

    private ToolWindow ShowNew(ToolKind kind, MainWindow owner)
    {
        var screen = ScreenOf(owner);
        var frame = FrameFor(kind, screen);
        var window = new ToolWindow(TitleOf(kind), ContentFor(kind), owner, remembered => _geometry.Remember(kind, remembered));
        window.Closed += (_, _) => Forget(kind, window);
        window.Present(frame);
        _open[kind.Id] = window;
        _geometry.Remember(kind, frame);
        return window;
    }

    private ToolWindowFrame FrameFor(ToolKind kind, PlacedScreen screen)
    {
        if (!_geometry.TryRecall(kind, out var saved))
        {
            saved = DefaultFrame(kind, screen);
        }

        return ToolWindowFrame.Clamp(saved, screen.Area);
    }

    private static ToolWindowFrame DefaultFrame(ToolKind kind, PlacedScreen screen)
    {
        var width = ToPx(kind.DefaultWidth, screen.Scale);
        var height = ToPx(kind.DefaultHeight, screen.Scale);
        var fitted = ToolWindowFrame.Clamp(new ToolWindowFrame(screen.Area.X, screen.Area.Y, width, height), screen.Area);
        var x = screen.Area.X + Math.Max(0, screen.Area.Width - fitted.Width) / 2;
        var y = screen.Area.Y + Math.Max(0, screen.Area.Height - fitted.Height) / 2;
        return new ToolWindowFrame(x, y, fitted.Width, fitted.Height);
    }

    private static int ToPx(int dips, double scale) => (int)Math.Round(dips * scale);

    private string TitleOf(ToolKind kind) => _localizer.Get(kind.TitleKey);

    private IShellWidget ContentFor(ToolKind kind)
    {
        if (!_views.TryGetValue(kind.Id, out var build))
        {
            return new NamedWidget("ToolSurface");
        }

        var content = build();
        if (content is null)
        {
            throw new InvalidOperationException($"Tool '{kind.Id}' returned no view.");
        }

        return content;
    }

    private void Forget(ToolKind kind, ToolWindow window)
    {
        if (_open.TryGetValue(kind.Id, out var current) && ReferenceEquals(current, window))
        {
            _open.Remove(kind.Id);
        }
    }

    private static PlacedScreen ScreenOf(MainWindow owner)
    {
        var screen = owner.Screen;
        var area = screen.WorkingArea;
        if (area.Width < 1 || area.Height < 1)
        {
            area = new ScreenRect(0, 0, 1920, 1080);
        }

        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        return new PlacedScreen(area, scale);
    }

    private readonly record struct PlacedScreen(ScreenRect Area, double Scale);
}
