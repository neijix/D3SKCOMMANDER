using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Archives;

public sealed class ArchivesModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<ArchivesModule>();
}
