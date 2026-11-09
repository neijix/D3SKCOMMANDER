using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Clipboard;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Panels;

/// <summary>
/// The panel commands that ask the user something or reach another service: groups, copied names,
/// the saved folders, the history, the branch view, the quick filter and the folder sizes.
/// </summary>
public sealed partial class PanelCommands
{
    private readonly PanelWorkspace _panels;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IClipboardText _clipboard;
    private readonly Startup.AppSettings _settings;
    private readonly Core.Jobs.IJobService _jobs;
    private readonly HashSet<string> _measuring = new(StringComparer.Ordinal);

    public PanelCommands(
        PanelWorkspace panels,
        IDialogService dialogs,
        ILocalizer text,
        IClipboardText clipboard,
        Startup.AppSettings settings,
        Core.Jobs.IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(jobs);
        _panels = panels;
        _dialogs = dialogs;
        _text = text;
        _clipboard = clipboard;
        _settings = settings;
        _jobs = jobs;
        _panels.FolderSizeRequested += OnFolderSizeRequested;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.SelectGroup, () => _ = SelectGroupAsync(true));
        shell.Handle(CommandId.UnselectGroup, () => _ = SelectGroupAsync(false));
        shell.Handle(CommandId.SelectSameExt, _panels.SelectSameExtension);
        shell.Handle(CommandId.CopyNames, () => _ = CopyAsync(paths: false));
        shell.Handle(CommandId.CopyPaths, () => _ = CopyAsync(paths: true));
        shell.Handle(CommandId.QuickFilter, _panels.OpenFilter);
        shell.Handle(CommandId.Branch, () => _ = _panels.ToggleBranchAsync());
        shell.Handle(CommandId.DirSizes, () => _ = DirSizesAsync());
        shell.Handle(CommandId.Hotlist, () => _ = HotlistAsync());
        shell.Handle(CommandId.AddHotlist, () => _ = AddHotlistAsync());
        shell.Handle(CommandId.AddToFavorites, () => _ = AddToFavoritesAsync());
        shell.Handle(CommandId.History, () => _ = HistoryAsync());
    }

    /// <summary>Asks for a wildcard mask and marks (or unmarks) the matching files of the active panel.</summary>
    public async Task SelectGroupAsync(bool select)
    {
        var title = _text.Get(select ? "panels.select" : "panels.unselect");
        var mask = await _dialogs.AskTextAsync(title, _text.Get("panels.mask"), "*").ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(mask))
        {
            _panels.SelectGroup(mask, select);
        }
    }

    /// <summary>Puts the names (or full paths) of the marked rows, or of the row under the cursor, on the clipboard.</summary>
    public async Task CopyAsync(bool paths)
    {
        var text = _panels.CopyText(paths);
        if (!string.IsNullOrEmpty(text))
        {
            await _clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
    }

    /// <summary>Measures the marked folders (else all folders) as a job; each size appears in the size column when it is known.</summary>
    public async Task DirSizesAsync()
    {
        var targets = _panels.FolderTargets();
        if (targets is null || targets.Value.Folders.Count == 0)
        {
            return;
        }

        var (side, directory, folders) = targets.Value;
        await MeasureAsync(side, directory, folders).ConfigureAwait(true);
    }

    // Space on a folder: one job per press, none while that folder is being measured; local folders only.
    private void OnFolderSizeRequested(object? sender, FolderSizeRequest request)
    {
        if (!Directory.Exists(request.Folder))
        {
            return;
        }

        lock (_measuring)
        {
            if (!_measuring.Add(request.Folder))
            {
                return;
            }
        }

        _ = MeasureThenReleaseAsync(request);
    }

    private async Task MeasureThenReleaseAsync(FolderSizeRequest request)
    {
        try
        {
            await MeasureAsync(request.Side, request.Directory, [request.Folder]).ConfigureAwait(true);
        }
        finally
        {
            lock (_measuring)
            {
                _measuring.Remove(request.Folder);
            }
        }
    }

    private async Task MeasureAsync(PanelSide side, string directory, IReadOnlyList<string> folders)
    {
        var items = folders.Select(folder => new Core.Jobs.JobItem(folder)).ToList();
        var job = _jobs.Start(
            _text.Get("panels.dirsizes"),
            items,
            async (item, progress, cancellationToken) =>
            {
                var stamp = Directory.GetLastWriteTimeUtc(item.Name);
                var size = await Task.Run(
                    () => Core.FileSystem.LocalFileSystem.DirectorySize(item.Name, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                _panels.SizeCache.Set(item.Name, stamp, size.Bytes);
                await _panels.ApplyFolderSizeAsync(side, directory, item.Name, size.Bytes, cancellationToken).ConfigureAwait(false);
                return Core.Jobs.ItemResult.Success;
            });
        await job.Completion.ConfigureAwait(true);
    }
}
