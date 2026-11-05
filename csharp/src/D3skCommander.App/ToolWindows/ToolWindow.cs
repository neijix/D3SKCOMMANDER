using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using D3skCommander.App.Shell;

namespace D3skCommander.App.ToolWindows;

/// <summary>One owned top-level tool window. Esc closes it, and its frame is remembered per kind.</summary>
public sealed class ToolWindow : Window, IOwnedWindow
{
    private readonly Action<ToolWindowFrame> _remember;
    private readonly MainWindow _main;
    private bool _quiet;

    public ToolWindow(string title, IShellWidget view, MainWindow owner, Action<ToolWindowFrame> remember)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(remember);
        _remember = remember;
        _main = owner;
        View = view;
        Scale = owner.Screen.Scaling <= 0 ? 1 : owner.Screen.Scaling;
        Title = title;
        Content = view.CreateContent();
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        AddHandler(KeyDownEvent, OnToolKeyDown, RoutingStrategies.Tunnel);
        PositionChanged += (_, _) => Remember();
        Closing += (_, _) => Remember();
    }

    /// <summary>The shell view this window shows.</summary>
    public IShellWidget View { get; }

    /// <summary>Device pixels per device-independent pixel of the owner's screen.</summary>
    public double Scale { get; }

    /// <summary>Applies the frame, attaches the window to its owner's list and shows it owned.</summary>
    public void Present(ToolWindowFrame frame)
    {
        Apply(frame);
        _main.Attach(this);
        Show(_main);
    }

    public void Apply(ToolWindowFrame frame)
    {
        RunQuiet(() =>
        {
            Width = frame.Width / Scale;
            Height = frame.Height / Scale;
            Position = new PixelPoint(frame.X, frame.Y);
        });
    }

    public ToolWindowFrame Read() => new(Position.X, Position.Y, ToPx(Width), ToPx(Height));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WidthProperty || change.Property == HeightProperty)
        {
            Remember();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _main.Detach(this);
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

    private void RunQuiet(Action action)
    {
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

    private int ToPx(double dips) => (int)Math.Round(dips * Scale);

    private void OnToolKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        e.Handled = true;
        Close();
    }
}
