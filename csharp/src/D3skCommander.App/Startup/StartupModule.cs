using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Startup;

public sealed class StartupModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<StartupModule>();
}
