using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Update;

namespace D3skCommander.App.Features.Update;

/// <summary>Help > Check for updates opens the Update window; start-up runs one quiet check.</summary>
public sealed class UpdateCommands
{
    public const string DisableVariable = "D3SK_NO_UPDATE_CHECK";

    private readonly UpdateChecker _checker;
    private readonly IUpdateFeed _feed;
    private readonly UpdateService _service;
    private readonly IUpdateRestarter _restarter;
    private readonly IToolWindowHost _tools;
    private readonly ILocalizer _text;
    private readonly PreferencesStore _preferences;
    private readonly Func<bool> _screenshotActive;
    private readonly Func<bool> _disabledByEnvironment;
    private readonly UpdateMenuItem _menuItem;

    public UpdateCommands(
        UpdateChecker checker,
        IUpdateFeed feed,
        UpdateService service,
        IUpdateRestarter restarter,
        IToolWindowHost tools,
        ILocalizer text,
        PreferencesStore preferences,
        Func<bool> screenshotActive,
        Func<bool> disabledByEnvironment)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(restarter);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(screenshotActive);
        ArgumentNullException.ThrowIfNull(disabledByEnvironment);
        _checker = checker;
        _feed = feed;
        _service = service;
        _restarter = restarter;
        _tools = tools;
        _text = text;
        _preferences = preferences;
        _screenshotActive = screenshotActive;
        _disabledByEnvironment = disabledByEnvironment;
        _menuItem = new UpdateMenuItem(Open);
    }

    public UpdateMenuItem MenuItem => _menuItem;

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.CheckUpdate, Open);
        shell.AddMenuBarTrailing(() => _menuItem);
        shell.OnStartup(() => _ = CheckAtStartAsync());
        _tools.Register(ToolKind.Update, CreateView);
    }

    public UpdateViewModel CreateView() => new(
        _checker,
        _feed,
        _service,
        _restarter,
        _text,
        Path.Combine(Path.GetTempPath(), "d3skcommander-update"),
        ShowFound);

    /// <summary>The quiet start-up check. A failure stays silent; a newer release shows the menu-bar item.</summary>
    public async Task<bool> CheckAtStartAsync()
    {
        if (!UpdateChecker.ShouldCheckAtStart(_preferences.Current.CheckUpdates, _screenshotActive(), _disabledByEnvironment()))
        {
            return false;
        }

        var check = await _checker.CheckAsync().ConfigureAwait(false);
        ShowFound(check.Outcome == UpdateOutcome.Newer ? check.Release : null);
        return true;
    }

    private void Open() => _tools.Open(ToolKind.Update);

    private void ShowFound(GitHubRelease? release)
    {
        var header = release is null
            ? ""
            : _text.Get("update.menu_item").Replace("{version}", release.Version, StringComparison.Ordinal);
        _menuItem.Set(header);
    }
}
