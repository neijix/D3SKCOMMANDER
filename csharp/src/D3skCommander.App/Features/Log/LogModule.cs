using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Log;

public sealed class LogModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<LogModule>();
}
