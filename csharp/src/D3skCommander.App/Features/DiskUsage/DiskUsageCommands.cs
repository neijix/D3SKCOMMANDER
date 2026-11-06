using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.DiskUsage;

/// <summary>
/// Shift+Alt+L opens the disk usage window for the folder under the cursor, or for the folder shown when the
/// cursor is on ".." or a file. Local folders only.
/// </summary>
public sealed class DiskUsageCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly IToolWindowHost _tools;
    private readonly DiskUsageViewModel _window;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;

    public DiskUsageCommands(
        IPanelWorkspace panels,
        IToolWindowHost tools,
        DiskUsageViewModel window,
        IDialogService dialogs,
        ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        _panels = panels;
        _tools = tools;
        _window = window;
        _dialogs = dialogs;
        _text = text;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.DiskUsage, () => _ = OpenAsync());
        _tools.Register(ToolKind.DiskUsage, () => _window);
    }

    public async Task OpenAsync()
    {
        var target = TargetFolder();
        if (target is null)
        {
            await _dialogs.ShowMessageAsync(_text.Get("tool.diskusage.title"), _text.Get("diskusage.not_local")).ConfigureAwait(true);
            return;
        }

        _tools.Open(ToolKind.DiskUsage);
        await _window.BeginAsync(target).ConfigureAwait(true);
    }

    /// <summary>The folder under the cursor, else the folder shown; null when that is not a local folder.</summary>
    private string? TargetFolder()
    {
        var snapshot = _panels.Read(_panels.ActiveSide);
        var folder = snapshot.Path;
        if (snapshot.Cursor >= 0 && snapshot.Cursor < snapshot.Entries.Count)
        {
            var entry = snapshot.Entries[snapshot.Cursor];
            if (entry.IsDirectory && !entry.IsParent)
            {
                folder = entry.Path;
            }
        }

        return folder.Length > 0 && Directory.Exists(folder) ? folder : null;
    }
}
