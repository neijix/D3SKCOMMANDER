using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Toolbar;

/// <summary>The main toolbar under the menu bar, and its ShowToolbar toggle. CustomizeToolbar belongs to the settings feature.</summary>
public sealed class ToolbarModule : IShellModule
{
    private readonly ToolbarModel _model;

    public ToolbarModule(ToolbarModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ToolbarModel>();
        services.AddSingleton<ToolbarModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<ToolbarModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _model.Bind(shell);
        shell.Handle(CommandId.ShowToolbar, _model.Toggle);
        shell.Checked(CommandId.ShowToolbar, () => _model.IsVisible);
        shell.AddRegion(ShellRegion.Toolbar, () => new ToolbarWidget(_model));
    }
}
