using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.ToolWindows;

public sealed class ToolWindowsModule
{
    public static void Register(IServiceCollection services) => services.AddSingleton<ToolWindowsModule>();
}
