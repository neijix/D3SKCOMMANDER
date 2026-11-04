using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Tests.Shell;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace D3skCommander.App.Tests.Shell;

public class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
