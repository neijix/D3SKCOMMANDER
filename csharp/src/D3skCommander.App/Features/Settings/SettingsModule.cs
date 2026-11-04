using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Settings;

public sealed class SettingsModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<SettingsModule>();
}
