using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Launch;

public sealed class LaunchModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<LaunchModule>();
}
