using D3skCommander.App.Shell;
using D3skCommander.Core.Search;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Search;

/// <summary>Find files (Alt+F7): the Search tool window.</summary>
public sealed class SearchModule : IShellModule
{
    private readonly SearchCommands _commands;

    public SearchModule(SearchCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(_ => new SearchEngine());
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<SearchCommands>();
        services.AddSingleton<SearchModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<SearchModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
