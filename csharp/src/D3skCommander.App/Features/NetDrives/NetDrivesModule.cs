using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.NetDrives;

public sealed class NetDrivesModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<NetDrivesModule>();
}
