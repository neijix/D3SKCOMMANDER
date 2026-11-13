using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Theme;

/// <summary>The Show menu theme entries and the saved preference switch the window colours live.</summary>
public class ThemeModuleTests
{
    [AvaloniaFact]
    public void TheThemeCommandsSwitchTheColoursSaveTheChoiceAndCheckTheirEntry()
    {
        using var host = ShellHost.Open(Language.En);
        var settings = host.Provider.GetRequiredService<AppSettings>();
        var shell = (ShellContributions)host.Provider.GetRequiredService<IShellContributions>();
        var app = Application.Current!;
        try
        {
            Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
            Assert.True(shell.CheckedState(CommandId.ThemeSystem));

            Assert.True(shell.Dispatch(CommandId.ThemeDark));
            Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Dark, host.Window.ActualThemeVariant);
            Assert.Equal(ThemeMode.Dark, settings.Preferences.Current.Theme);
            Assert.True(shell.CheckedState(CommandId.ThemeDark));
            Assert.False(shell.CheckedState(CommandId.ThemeSystem));

            Assert.True(shell.Dispatch(CommandId.ThemeLight));
            Assert.Equal(ThemeVariant.Light, host.Window.ActualThemeVariant);
            Assert.Equal(ThemeMode.Light, settings.Preferences.Current.Theme);

            Assert.True(shell.Dispatch(CommandId.ThemeSystem));
            Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
        }
        finally
        {
            Reset(settings, app);
        }
    }

    [AvaloniaFact]
    public void AThemeSavedBySettingsIsAppliedAtOnce()
    {
        using var host = ShellHost.Open(Language.En);
        var settings = host.Provider.GetRequiredService<AppSettings>();
        var app = Application.Current!;
        try
        {
            var preferences = settings.Preferences.Current;
            preferences.Theme = ThemeMode.Dark;
            settings.Preferences.Save(preferences);
            Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
        }
        finally
        {
            Reset(settings, app);
        }
    }

    private static void Reset(AppSettings settings, Application app)
    {
        var preferences = settings.Preferences.Current;
        preferences.Theme = ThemeMode.System;
        settings.Preferences.Save(preferences);
        app.RequestedThemeVariant = ThemeVariant.Default;
    }
}
