using D3skCommander.App.Shell;
using D3skCommander.Core.Checksums;
using D3skCommander.Core.Splitter;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Tools;

/// <summary>Split, combine, checksum and date dialogs. The work runs on the job service.</summary>
public sealed class ToolsModule : IShellModule
{
    private readonly ToolsCommands _commands;

    public ToolsModule(ToolsCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SplitterService>();
        services.AddSingleton<ChecksumService>();
        services.AddSingleton<ToolsCommands>();
        services.AddSingleton<ToolsModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<ToolsModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
