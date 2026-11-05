using D3skCommander.App.Shell;
using D3skCommander.Core.Archives;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Archives;

/// <summary>Pack, Unpack and Backup to ZIP: a form where needed, the work a job.</summary>
public sealed class ArchivesModule : IShellModule
{
    private readonly ArchiveCommands _commands;

    public ArchivesModule(ArchiveCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ArchiveService>();
        services.AddSingleton<ArchiveCommands>();
        services.AddSingleton<ArchivesModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<ArchivesModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
