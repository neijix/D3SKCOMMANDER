using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Smoke;

public sealed class SmokeModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<SmokeModule>();
}
