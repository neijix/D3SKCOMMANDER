using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Compare;

public sealed class CompareModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<CompareModule>();
}
