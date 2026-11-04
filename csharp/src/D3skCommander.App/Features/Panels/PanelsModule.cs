using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Panels;

public sealed class PanelsModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<PanelsModule>();
}
