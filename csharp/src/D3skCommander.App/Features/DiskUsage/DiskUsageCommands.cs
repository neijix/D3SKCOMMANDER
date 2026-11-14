using System.Globalization;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;

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
    private readonly IComputerSources _sources;
    private readonly IServerShares _servers;

    public DiskUsageCommands(
        IPanelWorkspace panels,
        IToolWindowHost tools,
        DiskUsageViewModel window,
        IDialogService dialogs,
        ILocalizer text,
        IComputerSources sources,
        IServerShares servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        _servers = servers;
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sources);
        _panels = panels;
        _tools = tools;
        _window = window;
        _dialogs = dialogs;
        _text = text;
        _sources = sources;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.DiskUsage, () => _ = OpenAsync());
        _tools.Register(ToolKind.DiskUsage, () => _window);
    }

    public async Task OpenAsync()
    {
        var snapshot = _panels.Read(_panels.ActiveSide);
        if (ComputerAddress.IsAddress(snapshot.Path))
        {
            var cursor = snapshot.Cursor >= 0 && snapshot.Cursor < snapshot.Entries.Count ? snapshot.Entries[snapshot.Cursor] : null;
            // The sources read drives and mounts: off the UI thread.
            var (scan, mounts) = await Task.Run(() =>
            {
                var target = new ComputerFileSystemProvider(_sources, _text).ScanTarget(cursor);
                return (target, target.Folder is null ? _sources.MountPoints() : []);
            }).ConfigureAwait(true);
            if (scan.Folder is null)
            {
                _tools.Open(ToolKind.DiskUsage);
                await _window.BeginDrivesAsync(scan.Drives, mounts).ConfigureAwait(true);
                return;
            }

            await BeginAsync(scan.Folder).ConfigureAwait(true);
            return;
        }

        var target = TargetFolder();
        if (target is null)
        {
            await _dialogs.ShowMessageAsync(_text.Get("tool.diskusage.title"), _text.Get("diskusage.not_local")).ConfigureAwait(true);
            return;
        }

        await BeginAsync(target).ConfigureAwait(true);
    }

    // A network server root (\\server\) has no folder of its own: its shares are listed (off the UI thread) and scanned.
    private async Task BeginAsync(string folder)
    {
        if (UncPath.Server(folder) is { } server && !Directory.Exists(folder))
        {
            var shares = await Task.Run(() => ServerScan.ShareRoots(folder, _servers)).ConfigureAwait(true);
            if (shares is not { IsSuccess: true } listed)
            {
                var failed = string.Format(CultureInfo.InvariantCulture, _text.Get("diskusage.failed"), folder);
                await _dialogs.ShowMessageAsync(_text.Get("tool.diskusage.title"), failed).ConfigureAwait(true);
                return;
            }

            _tools.Open(ToolKind.DiskUsage);
            await _window.BeginSharesAsync(UncPath.ServerRoot(server), listed.Value).ConfigureAwait(true);
            return;
        }

        _tools.Open(ToolKind.DiskUsage);
        await _window.BeginAsync(folder).ConfigureAwait(true);
    }

    /// <summary>The folder under the cursor, else the folder shown; null when that is not a local folder or a server.</summary>
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

        return folder.Length > 0 && (Directory.Exists(folder) || UncPath.Server(folder) is not null) ? folder : null;
    }
}
