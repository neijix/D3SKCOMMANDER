using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.NetDrives;
using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Panels;

public sealed class PanelsModule : IShellModule
{
    private readonly PanelWorkspace _panels;
    private readonly PanelCommands _commands;
    private readonly ILocalizer _text;
    private readonly IDialogService _dialogs;
    private readonly IDriveSource _drives;

    public PanelsModule(
        PanelWorkspace panels,
        PanelCommands commands,
        ILocalizer text,
        IDialogService dialogs,
        IDriveSource volumes,
        INetDriveRunner shares)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        _panels = panels;
        _commands = commands;
        _text = text;
        _dialogs = dialogs;
        _drives = new BarDriveSource(volumes, shares);
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IFileSystemProvider, LocalFileSystemProvider>();
        services.AddSingleton<PanelWorkspace>();
        services.AddSingleton<IPanelWorkspace>(provider => provider.GetRequiredService<PanelWorkspace>());
        services.AddSingleton<PanelCommands>();
        services.AddSingleton<PanelsModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<PanelsModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _panels.Bind(shell);
        _commands.Bind(shell);
        shell.SetPanel(PanelSide.Left, () => new PanelPaneModel(PanelSide.Left, _panels, _text, _dialogs, shell.Dispatch, _drives));
        shell.SetPanel(PanelSide.Right, () => new PanelPaneModel(PanelSide.Right, _panels, _text, _dialogs, shell.Dispatch, _drives));
    }
}
