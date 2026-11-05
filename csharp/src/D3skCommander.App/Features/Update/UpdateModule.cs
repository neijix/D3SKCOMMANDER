using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.App.Smoke;
using D3skCommander.App.Startup;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Update;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Update;

/// <summary>Help > Check for updates, the Update window and the quiet start-up check.</summary>
public sealed class UpdateModule : IShellModule
{
    private readonly UpdateCommands _commands;

    public UpdateModule(UpdateCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IUpdateFeed, HttpUpdateFeed>();
        services.AddSingleton<IUpdateRestarter, ProcessRestarter>();
        services.AddSingleton(_ => UpdateEnvironment.Current());
        services.AddSingleton<UpdateChecker>();
        services.AddSingleton(provider => new UpdateService(provider.GetRequiredService<IJobService>()));
        services.AddSingleton(provider => new UpdateCommands(
            provider.GetRequiredService<UpdateChecker>(),
            provider.GetRequiredService<IUpdateFeed>(),
            provider.GetRequiredService<UpdateService>(),
            provider.GetRequiredService<IUpdateRestarter>(),
            provider.GetRequiredService<IToolWindowHost>(),
            provider.GetRequiredService<ILocalizer>(),
            provider.GetRequiredService<AppSettings>().Preferences,
            () => provider.GetRequiredService<SmokeModule>().IsActive,
            () => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(UpdateCommands.DisableVariable))));
        services.AddSingleton<UpdateModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<UpdateModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
    }
}
