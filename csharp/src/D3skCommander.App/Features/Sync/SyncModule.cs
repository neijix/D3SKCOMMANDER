using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Sync;

public sealed class SyncModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<SyncModule>();
}
