using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Search;

public sealed class SearchModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<SearchModule>();
}
