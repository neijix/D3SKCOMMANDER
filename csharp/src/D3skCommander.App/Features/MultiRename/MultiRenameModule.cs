using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.MultiRename;

public sealed class MultiRenameModule : IShellModule
{
    private readonly MultiRenameCommands _commands;

    public MultiRenameModule(MultiRenameCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<RenameWindow>();
        services.AddSingleton<MultiRenameCommands>();
        services.AddSingleton<MultiRenameModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<MultiRenameModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
