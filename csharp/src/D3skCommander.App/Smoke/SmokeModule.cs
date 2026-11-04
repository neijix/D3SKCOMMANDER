using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Smoke;

/// <summary>
/// Developer hook. <c>D3SK_SCREENSHOT</c> saves a PNG after 2 s and quits.
/// <c>D3SK_SCREENSHOT_CMD</c> runs that command by name after 0.8 s.
/// Inactive when the screenshot variable is unset, so later start-up questions can skip themselves.
/// </summary>
public sealed class SmokeModule : IShellModule
{
    public const string ScreenshotVariable = "D3SK_SCREENSHOT";

    public const string CommandVariable = "D3SK_SCREENSHOT_CMD";

    public static readonly TimeSpan CommandDelay = TimeSpan.FromMilliseconds(800);

    public static readonly TimeSpan ShotDelay = TimeSpan.FromMilliseconds(2000);

    private readonly SmokeSession _session;

    private SmokeModule(SmokeSession session) => _session = session;

    public bool IsActive => _session.IsActive;

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(CreateSession);
        services.AddSingleton(sp => new SmokeModule(sp.GetRequiredService<SmokeSession>()));
        services.AddSingleton<IShellModule>(sp => sp.GetRequiredService<SmokeModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.OnStartup(_session.Start);
    }

    private static SmokeSession CreateSession(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new SmokeSession(
            services.GetRequiredService<ILocalizer>(),
            services.GetRequiredService<IShellContributions>(),
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            services.GetService<TextWriter>() ?? Console.Error);
    }
}
