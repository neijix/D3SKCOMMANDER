using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Drives;

/// <summary>Drive lists (Alt+F1, Alt+F2), Properties (Alt+Enter) and files dropped from the OS.</summary>
public sealed class DrivesModule : IShellModule
{
    private readonly DrivesCommands _commands;

    public DrivesModule(DrivesCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IDriveSource, OsDriveSource>();
        services.AddSingleton<DrivesCommands>();
        services.AddSingleton<DrivesModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<DrivesModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
