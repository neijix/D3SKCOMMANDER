using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Compare;

/// <summary>Compare files (tool window) and compare panels (dialog). Handlers register here.</summary>
public sealed class CompareModule : IShellModule
{
    private readonly CompareCommands _commands;
    private readonly IToolWindowHost _tools;

    public CompareModule(CompareCommands commands, IToolWindowHost tools)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(tools);
        _commands = commands;
        _tools = tools;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<CompareCommands>();
        services.AddSingleton<CompareModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<CompareModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
        _tools.Register(ToolKind.Compare, _commands.CreateView);
    }
}
