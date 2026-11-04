using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Drives;

public sealed class DrivesModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<DrivesModule>();
}
