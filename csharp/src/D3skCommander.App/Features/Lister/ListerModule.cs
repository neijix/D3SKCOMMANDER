using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Lister;

public sealed class ListerModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<ListerModule>();
}
