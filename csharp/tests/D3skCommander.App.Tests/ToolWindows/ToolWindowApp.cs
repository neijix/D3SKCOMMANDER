using D3skCommander.App;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.ToolWindows;

internal sealed class ToolWindowApp : IDisposable
{
    private readonly ServiceProvider _provider;

    private ToolWindowApp(ServiceProvider provider)
    {
        _provider = provider;
        Localizer = provider.GetRequiredService<ILocalizer>();
        Tools = provider.GetRequiredService<IToolWindowHost>();
        Window = provider.GetRequiredService<MainWindow>();
        Window.Show();
    }

    public ILocalizer Localizer { get; }

    public IToolWindowHost Tools { get; }

    public MainWindow Window { get; }

    public static ToolWindowApp Open()
    {
        var localizer = TextCatalog.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        return new ToolWindowApp(TestComposition.Build(localizer));
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
