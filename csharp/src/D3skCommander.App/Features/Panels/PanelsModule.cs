using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Panels;

public sealed class PanelsModule : IShellModule
{
    private readonly PanelWorkspace _panels;
    private readonly ILocalizer _text;
    private readonly IDialogService _dialogs;

    public PanelsModule(PanelWorkspace panels, ILocalizer text, IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        _panels = panels;
        _text = text;
        _dialogs = dialogs;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IFileSystemProvider, LocalFileSystemProvider>();
        services.AddSingleton<PanelWorkspace>();
        services.AddSingleton<IPanelWorkspace>(provider => provider.GetRequiredService<PanelWorkspace>());
        services.AddSingleton<PanelsModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<PanelsModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _panels.Bind(shell);
        shell.SetPanel(PanelSide.Left, () => new PanelPane(PanelSide.Left, _panels, _text, _dialogs));
        shell.SetPanel(PanelSide.Right, () => new PanelPane(PanelSide.Right, _panels, _text, _dialogs));
    }
}
