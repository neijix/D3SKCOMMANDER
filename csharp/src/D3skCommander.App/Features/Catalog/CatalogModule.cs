using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Catalog;

public sealed class CatalogModule : IShellModule
{
    private readonly CatalogCommands _commands;
    private readonly CatalogDialog _dialog;

    public CatalogModule(CatalogCommands commands, CatalogDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(dialog);
        _commands = commands;
        _dialog = dialog;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<CatalogDialog>();
        services.AddSingleton<CatalogCommands>();
        services.AddSingleton<CatalogModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<CatalogModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
        shell.AddRegion(ShellRegion.UnderPanels, () => _dialog);
    }
}
