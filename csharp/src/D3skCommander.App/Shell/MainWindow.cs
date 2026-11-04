using D3skCommander.App.Smoke;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Shell;

/// <summary>The commander window. ImGui draws it; the lists below are what that draw reads.</summary>
public sealed partial class MainWindow
{
    private readonly ShellContributions _shell;
    private readonly ILocalizer _localizer;
    private readonly List<IOwnedWindow> _owned = [];

    public MainWindow(ShellContributions shell, IEnumerable<IShellModule> modules, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(localizer);
        _shell = shell;
        _localizer = localizer;
        shell.ApplyModules(modules);
        shell.Handle(CommandId.Quit, Close);
        Title = localizer.Get("shell.window_title");
        StatusText = localizer.Get("st.ready");
        LeftPanel = new ShellSlot(localizer.Get("shell.panel.left"));
        RightPanel = new ShellSlot(localizer.Get("shell.panel.right"));
        Menus = BuildMenus();
        FunctionKeys = BuildFunctionKeys();
        UnderPanels = BuildWidgets(shell.UnderPanels);
        BesidePanels = BuildWidgets(shell.BesidePanels);
        MenuBarTrailing = BuildWidgets(shell.MenuBarTrailing);
        LeftPanel.Content = BuildOne(shell.LeftPanel);
        RightPanel.Content = BuildOne(shell.RightPanel);
        Bounds = new WindowBounds(1100, 700);
        Screen = new DesktopScreen(new ScreenRect(0, 0, 1920, 1080), 1);
    }

    public string Title { get; }

    public string StatusText { get; }

    public bool IsVisible { get; private set; }

    public WindowBounds Bounds { get; }

    public DesktopScreen Screen { get; }

    public ShellSlot LeftPanel { get; }

    public ShellSlot RightPanel { get; }

    public IReadOnlyList<ShellMenu> Menus { get; }

    public IReadOnlyList<FunctionButton> FunctionKeys { get; }

    public IReadOnlyList<IShellWidget> UnderPanels { get; }

    public IReadOnlyList<IShellWidget> BesidePanels { get; }

    public IReadOnlyList<IShellWidget> MenuBarTrailing { get; }

    public IReadOnlyList<IOwnedWindow> OwnedWindows => _owned;

    public bool Dispatch(CommandId command) => _shell.Dispatch(command);

    public void Show()
    {
        if (IsVisible)
        {
            return;
        }

        IsVisible = true;
        ToolWindowOwners.Opened(this);
        SmokeSession.NotifyOpened(this);
        _shell.RunStartup();
    }

    public void Close()
    {
        if (!IsVisible)
        {
            return;
        }

        foreach (var owned in _owned.ToArray())
        {
            owned.Close();
        }

        IsVisible = false;
        ToolWindowOwners.Closed(this);
    }

    public void KeyPress(GestureKey key, GestureModifiers modifiers)
    {
        var command = CommandCatalog.Match(new Gesture(modifiers, key));
        if (command is CommandId id)
        {
            _shell.Dispatch(id);
        }
    }

    public void HandleImGuiKeys()
    {
        var io = ImGui.GetIO();
        var modifiers = GestureMap.Modifiers(
            GestureMap.IsPrimary(io.KeyCtrl, io.KeySuper),
            io.KeyShift,
            io.KeyAlt);
        foreach (var key in WatchedKeys)
        {
            if (ImGui.IsKeyPressed(key) && GestureMap.From(key, modifiers) is Gesture gesture)
            {
                KeyPress(gesture.Key, gesture.Modifiers);
            }
        }
    }

    internal void Attach(IOwnedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _owned.Add(window);
    }

    internal void Detach(IOwnedWindow window) => _owned.Remove(window);

    private static readonly ImGuiKey[] WatchedKeys =
    [
        ImGuiKey.F1, ImGuiKey.F2, ImGuiKey.F3, ImGuiKey.F4, ImGuiKey.F5,
        ImGuiKey.F6, ImGuiKey.F7, ImGuiKey.F8, ImGuiKey.F9,
        ImGuiKey.Delete, ImGuiKey.Enter, ImGuiKey.KeypadEnter,
        ImGuiKey.LeftArrow, ImGuiKey.RightArrow, ImGuiKey.DownArrow,
        ImGuiKey.Tab, ImGuiKey.PageUp, ImGuiKey.Backspace,
        ImGuiKey.A, ImGuiKey.B, ImGuiKey.D, ImGuiKey.H, ImGuiKey.L,
        ImGuiKey.M, ImGuiKey.N, ImGuiKey.Q, ImGuiKey.R, ImGuiKey.S,
        ImGuiKey.T, ImGuiKey.U, ImGuiKey.W,
        ImGuiKey.Equal, ImGuiKey.Minus, ImGuiKey.KeypadAdd, ImGuiKey.KeypadSubtract,
        ImGuiKey.Backslash,
    ];

    private static IShellWidget? BuildOne(Func<IShellWidget>? build) => build is null ? null : Require(build);

    private static IReadOnlyList<IShellWidget> BuildWidgets(IReadOnlyList<Func<IShellWidget>> builds)
    {
        var widgets = new List<IShellWidget>(builds.Count);
        foreach (var build in builds)
        {
            widgets.Add(Require(build));
        }

        return widgets;
    }

    private static IShellWidget Require(Func<IShellWidget> build)
    {
        var widget = build();
        if (widget is null)
        {
            throw new InvalidOperationException("A shell view factory returned null.");
        }

        return widget;
    }
}

/// <summary>The main window's client size in device-independent pixels.</summary>
public readonly record struct WindowBounds(double Width, double Height);
