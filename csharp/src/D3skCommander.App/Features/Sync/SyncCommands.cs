using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Sync;

/// <summary>The Sync command opens the tool window with both panel folders as the roots.</summary>
public sealed class SyncCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly IJobService _jobs;
    private readonly ILocalizer _text;
    private readonly IFolderPicker _folders;
    private readonly IToolWindowHost _tools;

    public SyncCommands(
        IPanelWorkspace panels,
        IJobService jobs,
        ILocalizer text,
        IFolderPicker folders,
        IToolWindowHost tools)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(tools);
        _panels = panels;
        _jobs = jobs;
        _text = text;
        _folders = folders;
        _tools = tools;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Sync, () => _tools.Open(ToolKind.Sync));
    }

    public SyncViewModel CreateView()
    {
        var left = _panels.Read(PanelSide.Left).Path;
        var right = _panels.Read(PanelSide.Right).Path;
        return new SyncViewModel(left, right, _jobs, _text, _folders, ReloadPanels);
    }

    private void ReloadPanels() => _ = _panels.RefreshAsync();
}
