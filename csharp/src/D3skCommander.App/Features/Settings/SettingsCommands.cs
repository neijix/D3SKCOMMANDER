using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.OperationLog;

namespace D3skCommander.App.Features.Settings;

/// <summary>F9: the Settings tool window. Its folder button runs the SettingsFolder command.</summary>
public sealed class SettingsCommands
{
    private readonly AppSettings _settings;
    private readonly IOperationLog _log;
    private readonly ILocalizer _text;
    private readonly IDialogService _dialogs;
    private readonly IToolWindowHost _tools;
    private readonly SettingsFolderCommands _folder;

    public SettingsCommands(
        AppSettings settings,
        IOperationLog log,
        ILocalizer text,
        IDialogService dialogs,
        IToolWindowHost tools,
        SettingsFolderCommands folder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(folder);
        _settings = settings;
        _log = log;
        _text = text;
        _dialogs = dialogs;
        _tools = tools;
        _folder = folder;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Settings, () => _tools.Open(ToolKind.Settings));
        shell.Handle(CommandId.EditColumnSets, () =>
        {
            if (_tools.Open(ToolKind.Settings).View is SettingsViewModel model)
            {
                model.RequestColumns();
            }
        });
        shell.Handle(CommandId.CustomizeToolbar, () =>
        {
            if (_tools.Open(ToolKind.Settings).View is SettingsViewModel model)
            {
                model.RequestToolbar();
            }
        });
        shell.Handle(CommandId.ManageFavoriteTabs, () =>
        {
            if (_tools.Open(ToolKind.Settings).View is SettingsViewModel model)
            {
                model.RequestFavorites();
            }
        });
        _tools.Register(ToolKind.Settings, CreateView);
    }

    public SettingsViewModel CreateView() =>
        new(_settings, _log, _text, _dialogs, () => _ = _folder.ChangeAsync());
}
