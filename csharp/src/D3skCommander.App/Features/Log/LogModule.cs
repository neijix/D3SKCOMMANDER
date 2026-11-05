using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Log;

public sealed class LogModule : IShellModule
{
    private readonly LogCommands _commands;

    public LogModule(LogCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<LogCommands>();
        services.AddSingleton<LogModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<LogModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
