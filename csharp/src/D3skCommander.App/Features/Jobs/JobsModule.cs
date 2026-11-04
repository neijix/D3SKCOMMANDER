using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Jobs;

public sealed class JobsModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<JobsModule>();
}
