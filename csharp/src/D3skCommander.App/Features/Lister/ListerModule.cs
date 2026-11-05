using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Lister;

/// <summary>The viewer (F3): its command and its tool window.</summary>
public sealed class ListerModule : IShellModule
{
    private readonly ViewerCommands _commands;

    public ListerModule(ViewerCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ViewerCommands>();
        services.AddSingleton<ListerModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<ListerModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
