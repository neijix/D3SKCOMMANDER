using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using D3skCommander.App.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App;

public class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
