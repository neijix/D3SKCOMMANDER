using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.QuickView;

public sealed class QuickViewModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<QuickViewModule>();
}
