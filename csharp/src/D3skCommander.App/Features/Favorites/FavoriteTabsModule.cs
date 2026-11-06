using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Favorites;

/// <summary>Favorite tabs: save the open tabs under a name, open a saved set, and the "Favorite tabs" submenu of the Show menu.</summary>
public sealed class FavoriteTabsModule : IShellModule
{
    private readonly FavoriteTabsCommands _commands;

    public FavoriteTabsModule(FavoriteTabsCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<FavoriteTabsCommands>();
        services.AddSingleton<FavoriteTabsModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<FavoriteTabsModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
