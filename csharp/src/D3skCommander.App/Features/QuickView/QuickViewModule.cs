using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.QuickView;

/// <summary>Quick view (Ctrl+Q): the file under the cursor shown in the other panel.</summary>
public sealed class QuickViewModule : IShellModule
{
    private readonly QuickViewCommands _commands;

    public QuickViewModule(QuickViewCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<QuickViewCommands>();
        services.AddSingleton<FileOps.IFileHolder>(provider => provider.GetRequiredService<QuickViewCommands>());
        services.AddSingleton<QuickViewModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<QuickViewModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
