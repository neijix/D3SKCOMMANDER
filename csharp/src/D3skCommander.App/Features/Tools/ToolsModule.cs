using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Tools;

public sealed class ToolsModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<ToolsModule>();
}
