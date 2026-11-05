using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.ToolWindows;

public sealed class ToolWindowsModule
{
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ToolWindowsModule>();
        services.AddSingleton<SessionToolWindowGeometry>();
        services.AddSingleton<IToolWindowGeometry>(provider => provider.GetRequiredService<SessionToolWindowGeometry>());
        services.AddSingleton<ToolWindowHost>();
        services.AddSingleton<IToolWindowHost>(provider => provider.GetRequiredService<ToolWindowHost>());
    }
}
