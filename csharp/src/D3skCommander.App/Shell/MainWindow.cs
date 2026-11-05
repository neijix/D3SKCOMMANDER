using D3skCommander.App.Smoke;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Shell;

/// <summary>The commander window: the shell members and lifecycle. The Avalonia plumbing is in MainWindow.axaml.cs.</summary>
public sealed partial class MainWindow
{
    private readonly ShellContributions _shell;
    private readonly MainWindowModel _model;
    private readonly ILocalizer _localizer;
    private readonly List<IOwnedWindow> _owned = [];

    /// <summary>For the XAML loader and the previewer: an empty shell.</summary>
    public MainWindow()
        : this(new ShellContributions(), [], Localizer.Load())
    {
    }

    public MainWindow(ShellContributions shell, IEnumerable<IShellModule> modules, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(localizer);
        _shell = shell;
        _localizer = localizer;
        shell.ApplyModules(modules);
        shell.Handle(CommandId.Quit, () => Close());
        StatusText = localizer.Get("st.ready");
        LeftPanel = new ShellSlot(localizer.Get("shell.panel.left"));
        RightPanel = new ShellSlot(localizer.Get("shell.panel.right"));
        _model = new MainWindowModel(shell, localizer);
        UnderPanels = BuildWidgets(shell.UnderPanels);
        BesidePanels = BuildWidgets(shell.BesidePanels);
        MenuBarTrailing = BuildWidgets(shell.MenuBarTrailing);
        LeftPanel.Content = BuildOne(shell.LeftPanel);
        RightPanel.Content = BuildOne(shell.RightPanel);
        StartSize = new WindowSize(1100, 700);
        Build();
    }

    public string StatusText { get; }

    /// <summary>The size the window opens at; <c>Bounds</c> is the laid-out rectangle.</summary>
    public WindowSize StartSize { get; }

    /// <summary>The screen the window is on (else the primary one); 1920x1080 at 1 when none is reported.</summary>
    public DesktopScreen Screen => DesktopScreen.From(Screens.ScreenFromWindow(this) ?? Screens.Primary);

    public ShellSlot LeftPanel { get; }

    public ShellSlot RightPanel { get; }

    public IReadOnlyList<ShellMenu> Menus => _model.Menus;

    public IReadOnlyList<FunctionButton> FunctionKeys => _model.FunctionKeys;

    public IReadOnlyList<IShellWidget> UnderPanels { get; }

    public IReadOnlyList<IShellWidget> BesidePanels { get; }

    public IReadOnlyList<IShellWidget> MenuBarTrailing { get; }

    public new IReadOnlyList<IOwnedWindow> OwnedWindows => _owned;

    public bool Dispatch(CommandId command) => _shell.Dispatch(command);

    /// <summary>The window opened: the owners list, the smoke notification, then the startup actions once.</summary>
    private void HandleOpened()
    {
        ToolWindowOwners.Opened(this);
        SmokeSession.NotifyOpened(this);
        _shell.RunStartup();
    }

    /// <summary>The window is closing: the owned windows first.</summary>
    private void CloseOwnedWindows()
    {
        foreach (var owned in _owned.ToArray())
        {
            owned.Close();
        }
    }

    public void KeyPress(GestureKey key, GestureModifiers modifiers)
    {
        _model.KeyPress(key, modifiers);
    }

    internal void Attach(IOwnedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _owned.Add(window);
    }

    internal void Detach(IOwnedWindow window) => _owned.Remove(window);

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
public readonly record struct WindowSize(double Width, double Height);
