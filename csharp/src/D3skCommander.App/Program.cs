using Avalonia;
using Avalonia.Controls;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var services = BuildProvider();
        BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(
            args,
            lifetime => lifetime.Startup += (_, _) => lifetime.MainWindow = services.GetRequiredService<MainWindow>());
    }

    /// <summary>
    /// The desktop Avalonia application on <paramref name="services"/>. <c>D3SK_RENDERING=software</c>
    /// makes every platform draw on the CPU (<see cref="Rendering"/>).
    /// </summary>
    public static AppBuilder BuildAvaloniaApp(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var builder = AppBuilder.Configure(() => new App(services)).UsePlatformDetect();
        return Rendering.FromEnvironment() == RenderingChoice.Software ? WithSoftwareRendering(builder) : builder;
    }

    // Each option only applies on its own platform; the other two are ignored.
    private static AppBuilder WithSoftwareRendering(AppBuilder builder) => builder
        .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
        .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
        .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] });

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
