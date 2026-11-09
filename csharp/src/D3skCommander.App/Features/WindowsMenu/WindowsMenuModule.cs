using Avalonia;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.WindowsMenu;

/// <summary>"More Windows options": the native Explorer menu for the target or the marked set, then a panel refresh.</summary>
public sealed class WindowsMenuModule : IShellModule
{
    private readonly IPanelWorkspace _panels;
    private readonly IExplorerMenu _menu;
    private readonly IDialogOwner _owner;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private Action<string>? _status;

    public WindowsMenuModule(IPanelWorkspace panels, IExplorerMenu menu, IDialogOwner owner, IDialogService dialogs, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        _panels = panels;
        _menu = menu;
        _owner = owner;
        _dialogs = dialogs;
        _text = text;
    }

    /// <summary>Whether this is Windows; tests change it.</summary>
    public Func<bool> IsWindows { get; set; } = OperatingSystem.IsWindows;

    /// <summary>
    /// Where the menu opens, in screen pixels: the context menu's click, or the cursor row's bottom-left for a key or the menu bar.
    /// Null (or unset) = the mouse.
    /// </summary>
    public Func<PixelPoint?>? ScreenPoint { get; set; }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IExplorerMenu, WindowsExplorerMenu>();
        services.AddSingleton<WindowsMenuModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<WindowsMenuModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _status = shell.SetStatus;
        shell.Handle(CommandId.WindowsMenu, Show);
    }

    /// <summary>Shows the native menu on the context target (else the cursor entry); invoked refreshes the panels, failed shows a dialog.</summary>
    public void Show()
    {
        if (!IsWindows())
        {
            _status?.Invoke(_text.Get("windowsmenu.windows_only"));
            return;
        }

        // The target is read first: a context menu's scope ends with its dispatch.
        var paths = _panels.ContextTarget is { } target ? ContextTargetPaths.Of(target) : _panels.Capture()?.Sources;
        if (paths is not { Count: > 0 })
        {
            return;
        }

        var owner = _owner.Current?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var result = _menu.Show(paths, ScreenPoint?.Invoke(), owner);
        switch (result.Outcome)
        {
            case ExplorerMenuOutcome.Invoked:
                _ = _panels.RefreshAsync();
                break;
            case ExplorerMenuOutcome.Failed:
                _ = _dialogs.ShowMessageAsync(_text.Get("windowsmenu.failed"), result.Detail ?? string.Empty);
                break;
        }
    }
}
