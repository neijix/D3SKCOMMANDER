using System.Numerics;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using Hexa.NET.ImGui;

namespace D3skCommander.App.ToolWindows;

/// <summary>One owned top-level tool window. Esc closes it. ImGui gives it its own platform window.</summary>
public sealed class ToolWindow : IOwnedWindow
{
    private readonly Action<ToolWindowFrame> _remember;
    private readonly string _id;
    private double _width;
    private double _height;
    private bool _quiet;

    public ToolWindow(string id, string title, IShellWidget content, MainWindow owner, Action<ToolWindowFrame> remember)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(remember);
        _id = id;
        _remember = remember;
        Title = title;
        Content = content;
        Owner = owner;
        Scale = owner.Screen.Scaling <= 0 ? 1 : owner.Screen.Scaling;
    }

    public string Title { get; }

    public IShellWidget Content { get; }

    public MainWindow Owner { get; }

    public object? Parent => null;

    public bool IsVisible { get; private set; }

    public double Scale { get; }

    public ScreenPoint Position
    {
        get => new(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
            Remember();
        }
    }

    public double Width
    {
        get => _width;
        set
        {
            _width = value;
            Remember();
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            _height = value;
            Remember();
        }
    }

    internal int X { get; private set; }

    internal int Y { get; private set; }

    public void Show()
    {
        IsVisible = true;
        Owner.Attach(this);
    }

    public void Close()
    {
        if (!IsVisible)
        {
            return;
        }

        Remember();
        IsVisible = false;
        Owner.Detach(this);
        Closed?.Invoke();
    }

    internal event Action? Closed;

    public void KeyPress(GestureKey key, GestureModifiers modifiers = GestureModifiers.None)
    {
        if (key == GestureKey.Escape && modifiers == GestureModifiers.None)
        {
            Close();
        }
    }

    public void Apply(ToolWindowFrame frame)
    {
        RunQuiet(() =>
        {
            X = frame.X;
            Y = frame.Y;
            _width = frame.Width / Scale;
            _height = frame.Height / Scale;
        });
    }

    public ToolWindowFrame Read() => new(X, Y, ToPx(_width), ToPx(_height));

    public void Draw()
    {
        if (!IsVisible)
        {
            return;
        }

        ImGui.SetNextWindowPos(new Vector2(X, Y), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(ToPx(_width), ToPx(_height)), ImGuiCond.Appearing);
        SetOwnViewport();
        var open = true;
        if (ImGui.Begin(Title + "###" + _id, ref open))
        {
            Content.Draw();
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            RunQuiet(() =>
            {
                X = (int)Math.Round(pos.X);
                Y = (int)Math.Round(pos.Y);
                _width = size.X / Scale;
                _height = size.Y / Scale;
            });
            Remember();
            if (ImGui.IsWindowFocused() && ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                Close();
            }
        }

        ImGui.End();
        if (!open)
        {
            Close();
        }
    }

    private void Remember()
    {
        if (_quiet || _width < 1 || _height < 1)
        {
            return;
        }

        _remember(Read());
    }

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

    private static unsafe void SetOwnViewport()
    {
        var windowClass = new ImGuiWindowClass();
        windowClass.ViewportFlagsOverrideSet = ImGuiViewportFlags.NoAutoMerge;
        ImGui.SetNextWindowClass(&windowClass);
    }
}
