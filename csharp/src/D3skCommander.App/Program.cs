using Avalonia;
using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var services = BuildProvider();
        BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(BuildProvider());

    public static AppBuilder BuildAvaloniaApp(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AppBuilder.Configure(() => new App(services)).UsePlatformDetect().LogToTrace();
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
