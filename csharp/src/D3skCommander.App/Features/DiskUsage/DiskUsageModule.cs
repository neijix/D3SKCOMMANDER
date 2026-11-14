using D3skCommander.App.Shell;
using D3skCommander.Core.NetDrives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace D3skCommander.App.Features.DiskUsage;

/// <summary>Disk usage (Shift+Alt+L): the folder size ring chart tool window.</summary>
public sealed class DiskUsageModule : IShellModule
{
    private readonly DiskUsageCommands _commands;

    public DiskUsageModule(DiskUsageCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IServerShares, OsServerShares>();
        services.AddSingleton<DiskUsageViewModel>();
        services.AddSingleton<DiskUsageCommands>();
        services.AddSingleton<DiskUsageModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<DiskUsageModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
