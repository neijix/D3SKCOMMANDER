using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Launch;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Launch;

/// <summary>Open with, terminal here, the file launcher and the command line under the panels.</summary>
public sealed class LaunchModule : IShellModule
{
    private readonly LaunchCommands _commands;
    private readonly CommandLineBarWidget _bar;
    private readonly IPanelWorkspace _panels;

    public LaunchModule(LaunchCommands commands, CommandLineBarWidget bar, IPanelWorkspace panels)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(bar);
        ArgumentNullException.ThrowIfNull(panels);
        _commands = commands;
        _bar = bar;
        _panels = panels;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<LaunchCommands>();
        services.AddSingleton<IFileLauncher>(provider => provider.GetRequiredService<LaunchCommands>());
        services.AddSingleton<CommandLineBarWidget>();
        services.AddSingleton<LaunchModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<LaunchModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.OpenWith, () => _ = _commands.OpenWithAsync());
        shell.Handle(CommandId.Terminal, _commands.OpenTerminal);
        shell.AddRegion(ShellRegion.UnderPanels, () => _bar);
        shell.AppendDynamicItems("menu.tools", _commands.UserCommandItems);
        if (_panels is PanelWorkspace workspace)
        {
            // Enter on a file the panel cannot enter opens it with its default application.
            workspace.OpenFile = path => _commands.OpenDefault(path);
        }
    }
}
