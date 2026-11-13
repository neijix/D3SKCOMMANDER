using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Features.Theme;

/// <summary>
/// The window colours: applies <see cref="Preferences.Theme"/> at start and whenever the preferences are
/// saved (the settings window or a Show menu theme entry), so the switch is live.
/// </summary>
public sealed class ThemeModule : IShellModule
{
    private readonly AppSettings _settings;

    public ThemeModule(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ThemeModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<ThemeModule>());
    }

    /// <summary>What Avalonia is asked for: <c>Default</c> follows the OS.</summary>
    public static ThemeVariant VariantOf(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ThemeVariant.Light,
        ThemeMode.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        foreach (var mode in ThemeModes.All)
        {
            shell.Handle(ThemeModes.Command(mode), () => Choose(mode));
            shell.Checked(ThemeModes.Command(mode), () => _settings.Preferences.Current.Theme == mode);
        }

        _settings.Preferences.Saved += OnSaved;
        Apply(_settings.Preferences.Current.Theme);
    }

    private void Choose(ThemeMode mode)
    {
        var preferences = _settings.Preferences.Current;
        if (preferences.Theme == mode)
        {
            return;
        }

        preferences.Theme = mode;
        _settings.Preferences.Save(preferences);
    }

    private void OnSaved(Preferences saved)
    {
        var mode = saved.Theme;
        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply(mode);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Apply(mode));
        }
    }

    private static void Apply(ThemeMode mode)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = VariantOf(mode);
        }
    }
}
