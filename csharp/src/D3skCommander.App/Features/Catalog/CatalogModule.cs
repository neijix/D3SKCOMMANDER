using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Catalog;

public sealed class CatalogModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<CatalogModule>();
}
