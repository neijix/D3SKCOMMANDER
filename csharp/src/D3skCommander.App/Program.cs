using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        _ = args;
        using var services = BuildProvider();
        using var host = new GlfwOpenGlHost(services);
        host.Run();
    }

    /// <summary>The one composition root. Features are added in <see cref="AppComposition"/>, not here.</summary>
    public static ServiceProvider BuildProvider(ILocalizer? localizer = null, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        ILocalizer text = localizer ?? Localizer.Load();
        services.AddSingleton(text);
        services.AddSingleton<ShellContributions>();
        services.AddSingleton<IShellContributions>(provider => provider.GetRequiredService<ShellContributions>());
        AppComposition.Register(services);
        services.AddTransient<MainWindow>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
