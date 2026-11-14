using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Toolbar;

/// <summary>
/// The main toolbar under the menu bar and the button column between the panels, with their
/// ShowToolbar and ShowMiddleBar toggles. CustomizeToolbar belongs to the settings feature.
/// </summary>
public sealed class ToolbarModule : IShellModule
{
    private readonly ToolbarModel _model;
    private readonly MiddleBarModel _middle;

    public ToolbarModule(ToolbarModel model, MiddleBarModel middle)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(middle);
        _model = model;
        _middle = middle;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ToolbarModel>();
        services.AddSingleton<MiddleBarModel>();
        services.AddSingleton<ToolbarModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<ToolbarModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _model.Bind(shell);
        _middle.Bind(shell);
        shell.Handle(CommandId.ShowToolbar, _model.Toggle);
        shell.Checked(CommandId.ShowToolbar, () => _model.IsVisible);
        shell.Handle(CommandId.ShowMiddleBar, _middle.Toggle);
        shell.Checked(CommandId.ShowMiddleBar, () => _middle.IsVisible);
        shell.AddRegion(ShellRegion.Toolbar, () => new ToolbarWidget(_model));
        shell.AddRegion(ShellRegion.BetweenPanels, () => new MiddleBarWidget(_middle));
    }
}
