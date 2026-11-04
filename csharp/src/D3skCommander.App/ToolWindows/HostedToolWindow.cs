using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace D3skCommander.App.ToolWindows;

/// <summary>The real top-level window for one tool kind. Esc closes it.</summary>
internal sealed class HostedToolWindow : Window
{
    private readonly Action<ToolWindowFrame> _remember;
    private bool _quiet;

    public HostedToolWindow(string title, Control content, Action<ToolWindowFrame> remember)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(remember);
        _remember = remember;
        Title = title;
        Content = content;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        PositionChanged += (_, _) => Remember();
        Resized += (_, _) => Remember();
        Closing += (_, _) => Remember();
    }

    public void Apply(ToolWindowFrame frame)
    {
        var scale = Scale();
        Width = frame.Width / scale;
        Height = frame.Height / scale;
        Position = new PixelPoint(frame.X, frame.Y);
    }

    public ToolWindowFrame Read()
    {
        var scale = Scale();
        return new ToolWindowFrame(Position.X, Position.Y, ToPx(Width, scale), ToPx(Height, scale));
    }

    public void RunQuiet(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _quiet = true;
        try
        {
            action();
        }
        finally
        {
            _quiet = false;
        }
    }

    private void Remember()
    {
        if (_quiet || !HasSize())
        {
            return;
        }

        _remember(Read());
    }

    private bool HasSize() => !double.IsNaN(Width) && !double.IsNaN(Height) && Width >= 1 && Height >= 1;

    private double Scale() => DesktopScaling > 0 ? DesktopScaling : 1;

    private static int ToPx(double dips, double scale) => (int)Math.Round(dips * scale);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        e.Handled = true;
        Close();
    }
}
