using D3sk.Update.UpdateClient;
using D3skCommander.App.Features.Update.SignIn;
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
    private readonly UpdateService _service;
    private readonly IUpdateRestarter _restarter;
    private readonly IToolWindowHost _tools;
    private readonly ILocalizer _text;
    private readonly PreferencesStore _preferences;
    private readonly Func<bool> _screenshotActive;
    private readonly Func<bool> _disabledByEnvironment;
    private readonly UpdateBadge _badge;
    private readonly IJobService _jobs;
    private readonly IUrlOpener _opener;

    public UpdateCommands(
        UpdateChecker checker,
        UpdateService service,
        IUpdateRestarter restarter,
        IToolWindowHost tools,
        ILocalizer text,
        PreferencesStore preferences,
        Func<bool> screenshotActive,
        Func<bool> disabledByEnvironment,
        IJobService? jobs = null,
        IUrlOpener? opener = null)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(restarter);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(screenshotActive);
        ArgumentNullException.ThrowIfNull(disabledByEnvironment);
        _checker = checker;
        _service = service;
        _restarter = restarter;
        _tools = tools;
        _text = text;
        _preferences = preferences;
        _screenshotActive = screenshotActive;
        _disabledByEnvironment = disabledByEnvironment;
        _jobs = jobs ?? new JobService();
        _opener = opener ?? new ProcessUrlOpener();
        _badge = new UpdateBadge(checker.Environment.Version, text.Get("update.current"), Open);
    }

    public UpdateBadge Badge => _badge;

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.CheckUpdate, Open);
        shell.AddMenuBarTrailing(() => _badge);
        shell.OnStartup(() => _ = CheckAtStartAsync());
        _tools.Register(ToolKind.Update, CreateView);
    }

    public UpdateViewModel CreateView()
    {
        SignInPanel? panel = null;
        var model = new UpdateViewModel(
            _checker,
            _service,
            _restarter,
            _text,
            Path.Combine(Path.GetTempPath(), "d3skcommander-update"),
            check =>
            {
                ShowFound(check);
                panel?.Observe(check);
            });
        panel = new SignInPanel(new SignInFlow(_service, _jobs, _opener), _text, model.SignInLine, model.CheckAsync);
        model.SignInPanel = panel;
        return model;
    }

    /// <summary>The quiet start-up check. A failure stays silent; a newer release shows the Update button.</summary>
    public async Task<bool> CheckAtStartAsync()
    {
        if (!UpdateChecker.ShouldCheckAtStart(_preferences.Current.CheckUpdates, _screenshotActive(), _disabledByEnvironment()))
        {
            return false;
        }

        var check = await _checker.CheckAsync().ConfigureAwait(false);
        ShowFound(check);
        return true;
    }

    private void Open() => _tools.Open(ToolKind.Update);

    /// <summary>
    /// The top-right Update button: "Update to X" for a newer signed release, "sign in needed" when the quiet check met a private
    /// repository without a token (it never starts the sign-in itself), else hidden.
    /// </summary>
    private void ShowFound(UpdateCheck check)
    {
        var newer = check.Status == CheckStatus.Newer && check.Offer is not null;
        var header = check.Status switch
        {
            CheckStatus.Newer when check.Offer is { } offer =>
                _text.Get("update.menu_item").Replace("{version}", offer.Version, StringComparison.Ordinal),
            CheckStatus.SignInNeeded => _text.Get("update.sign_in_needed"),
            _ => "",
        };
        _badge.Set(header, newer);
    }
}
