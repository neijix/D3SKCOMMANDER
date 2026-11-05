using D3skCommander.App.Shell;
using D3skCommander.App.Smoke;
using D3skCommander.Core.SettingsLocation;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Settings;

/// <summary>Settings (F9), the settings folder, and the first-launch question as a start-up action.</summary>
public sealed class SettingsModule : IShellModule
{
    private readonly SettingsCommands _commands;
    private readonly SettingsFolderCommands _folder;

    public SettingsModule(SettingsCommands commands, SettingsFolderCommands folder)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(folder);
        _commands = commands;
        _folder = folder;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(provider => new SettingsFolderCommands(
            provider.GetRequiredService<Startup.AppSettings>(),
            provider.GetRequiredService<Dialogs.IDialogService>(),
            provider.GetRequiredService<Core.Localization.ILocalizer>(),
            () => provider.GetRequiredService<SmokeModule>().IsActive,
            SettingsPlaces.Current));
        services.AddSingleton<SettingsCommands>();
        services.AddSingleton<SettingsModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<SettingsModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _commands.Bind(shell);
        _folder.Bind(shell);
    }
}
