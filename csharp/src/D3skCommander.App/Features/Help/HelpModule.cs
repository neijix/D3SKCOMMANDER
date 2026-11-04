using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Help;

public sealed class HelpModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<HelpModule>();
}
