using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Editor;

/// <summary>The editor (F4, Shift+F4): its commands and its tool window.</summary>
public sealed class EditorModule : IShellModule
{
    private readonly EditorCommands _commands;

    public EditorModule(EditorCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<EditorCommands>();
        services.AddSingleton<EditorModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<EditorModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
