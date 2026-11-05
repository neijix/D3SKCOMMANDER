using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.NetDrives;

/// <summary>Network drives: the connect form and the disconnect list.</summary>
public sealed class NetDrivesModule : IShellModule
{
    private readonly NetDrivesCommands _commands;

    public NetDrivesModule(NetDrivesCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<INetDriveRunner, OsNetDriveRunner>();
        services.AddSingleton<NetDrivesCommands>();
        services.AddSingleton<NetDrivesModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<NetDrivesModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
