using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Tests;
using Microsoft.Extensions.DependencyInjection;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace D3skCommander.App.Tests;

/// <summary>The headless Avalonia app for [AvaloniaFact] tests: the app's own <c>App</c>, Fluent loaded.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure(() => new App(Program.BuildProvider()))
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
