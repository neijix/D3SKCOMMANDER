using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Update;

public sealed class UpdateModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<UpdateModule>();
}
