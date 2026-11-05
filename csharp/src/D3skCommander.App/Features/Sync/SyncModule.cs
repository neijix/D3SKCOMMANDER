using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Sync;

/// <summary>Synchronize directories (tool window). Its handler registers here.</summary>
public sealed class SyncModule : IShellModule
{
    private readonly SyncCommands _commands;
    private readonly IToolWindowHost _tools;

    public SyncModule(SyncCommands commands, IToolWindowHost tools)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(tools);
        _commands = commands;
        _tools = tools;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SyncCommands>();
        services.AddSingleton<SyncModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<SyncModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
        _tools.Register(ToolKind.Sync, _commands.CreateView);
    }
}
