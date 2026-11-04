using D3skCommander.App;
using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Shell;

internal sealed class ShellHost : IDisposable
{
    private readonly ServiceProvider _provider;

    private ShellHost(ServiceProvider provider)
    {
        _provider = provider;
        Localizer = provider.GetRequiredService<ILocalizer>();
        Window = provider.GetRequiredService<MainWindow>();
        Window.Show();
    }

    public ILocalizer Localizer { get; }

    public MainWindow Window { get; }

    public static ShellHost Open(Language language, Action<IServiceCollection>? configure = null)
    {
        var localizer = TextCatalog.Load(settingsLanguage: language, systemLanguage: language);
        return new ShellHost(Program.BuildProvider(localizer, configure));
    }

    public void Dispose()
    {
        if (Window.IsVisible)
        {
            Window.Close();
        }

        _provider.Dispose();
    }
}
