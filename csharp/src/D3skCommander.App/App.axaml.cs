using Avalonia;
using Avalonia.Markup.Xaml;

namespace D3skCommander.App;

/// <summary>The Avalonia application: Fluent theme following the OS light/dark setting.
/// <see cref="Program"/> starts it.</summary>
public class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>The composition root this application was built with.</summary>
    public IServiceProvider Services => _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
