using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Shell;

public partial class MainWindow : Window
{
    private readonly ShellContributions _shell;
    private readonly ILocalizer _localizer;

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
        shell.Handle(CommandId.Quit, Quit);
        InitializeComponent();
        Title = localizer.Get("shell.window_title");
        BuildMenu();
        BuildChrome();
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
    }

    public bool Dispatch(CommandId command) => _shell.Dispatch(command);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _shell.RunStartup();
    }

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || FocusedElementIsText())
        {
            return;
        }

        var gesture = GestureMap.From(e.Key, e.KeyModifiers);
        if (gesture is null)
        {
            return;
        }

        var command = CommandCatalog.Match(gesture.Value);
        if (command is CommandId id && _shell.Dispatch(id))
        {
            e.Handled = true;
        }
    }

    private bool FocusedElementIsText()
    {
        var focused = FocusManager?.GetFocusedElement();
        return focused is TextBox;
    }

    private void Quit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        Close();
    }
}
