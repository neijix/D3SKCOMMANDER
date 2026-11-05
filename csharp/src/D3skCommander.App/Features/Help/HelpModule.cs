using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Help;

public sealed class HelpModule : IShellModule
{
    private readonly HelpCommands _commands;

    public HelpModule(HelpCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<HelpCommands>();
        services.AddSingleton<HelpModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<HelpModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
