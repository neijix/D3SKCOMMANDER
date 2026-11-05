using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Startup;

/// <summary>
/// Start-up wiring: the settings folder (<c>D3SK_CONFIG_DIR</c>, portable, pointer, platform, install),
/// its stores, the operations log in <c>ops.log</c> there, and the temp workspace.
/// A store that failed to load shows its error once after start.
/// </summary>
public sealed class StartupModule : IShellModule
{
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;

    public StartupModule(AppSettings settings, IDialogService dialogs, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        _settings = settings;
        _dialogs = dialogs;
        _text = text;
    }

    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(_ => AppSettings.ForThisProcess());
        services.AddSingleton<IOperationLog>(provider =>
        {
            var settings = provider.GetRequiredService<AppSettings>();
            var log = new OperationLog();
            log.UseSettingsFolder(settings.Directory);
            log.Enabled = settings.Preferences.Current.LogOperations;
            return log;
        });
        services.AddSingleton<ITempWorkspace>(_ => new TempWorkspace());
        services.AddSingleton<StartupModule>();
        services.AddSingleton<IShellModule>(provider => provider.GetRequiredService<StartupModule>());
    }

    public void Contribute(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.OnStartup(() => _ = ShowLoadErrorsAsync());
    }

    private async Task ShowLoadErrorsAsync()
    {
        foreach (var error in _settings.TakeLoadErrors())
        {
            await _dialogs.ShowMessageAsync(_text.Get("error"), ErrorLines.Describe(_text, error)).ConfigureAwait(false);
        }
    }
}
