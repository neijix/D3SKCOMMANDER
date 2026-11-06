using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Associations;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Associations;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Lister;

/// <summary>
/// View (F3) opens the first file of the active panel's selection in the lister window.
/// Folders are skipped; no file shows the no-selection message. A window already open is reused.
/// </summary>
public sealed class ViewerCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly IToolWindowHost _tools;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly IFileAssociations? _associations;

    public ViewerCommands(IPanelWorkspace panels, IToolWindowHost tools, IDialogService dialogs, ILocalizer text, IJobService jobs, IFileAssociations? associations = null)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        _panels = panels;
        _tools = tools;
        _dialogs = dialogs;
        _text = text;
        _jobs = jobs;
        _associations = associations;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.View, () => _ = ViewAsync());
        _tools.Register(ToolKind.Lister, () => new ViewerViewModel(_text, _jobs));
    }

    /// <summary>Opens the window, then loads the file; the task ends when the file is shown.</summary>
    public Task ViewAsync()
    {
        var capture = _panels.Capture();
        if (capture is null)
        {
            return Task.CompletedTask;
        }

        var path = FirstFile(capture.Sources);
        if (path is null)
        {
            return _dialogs.ShowMessageAsync(_text.Get("cmd.view"), _text.Get("dlg.no_selection"));
        }

        if (_associations?.TryStart(AssociationKind.View, path) is { } started)
        {
            return started;
        }

        var window = _tools.Open(ToolKind.Lister);
        return window.View is ViewerViewModel model ? model.OpenAsync(path) : Task.CompletedTask;
    }

    private static string? FirstFile(IReadOnlyList<string> sources)
    {
        foreach (var source in sources)
        {
            if (!Directory.Exists(source))
            {
                return source;
            }
        }

        return null;
    }
}
