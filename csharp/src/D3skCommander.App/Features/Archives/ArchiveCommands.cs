using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Archives;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Archives;

/// <summary>Alt+F5, Alt+F9 and the backup command. One at a time; the file work is a job.</summary>
public sealed partial class ArchiveCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly ArchiveService _archives;
    private int _busy;

    public ArchiveCommands(IPanelWorkspace panels, IDialogService dialogs, ILocalizer text, ArchiveService archives)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(archives);
        _panels = panels;
        _dialogs = dialogs;
        _text = text;
        _archives = archives;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Pack, () => _ = PackAsync());
        shell.Handle(CommandId.Unpack, () => _ = UnpackAsync());
        shell.Handle(CommandId.BackupZip, () => _ = BackupAsync());
    }

    /// <param name="cancellationToken">Stops the job as the Cancel button of the jobs list does.</param>
    public Task PackAsync(CancellationToken cancellationToken = default) =>
        Run(() => PackCore(cancellationToken));

    public Task UnpackAsync(CancellationToken cancellationToken = default) =>
        Run(() => UnpackCore(cancellationToken));

    public Task BackupAsync(CancellationToken cancellationToken = default) =>
        Run(() => BackupCore(cancellationToken));

    private async Task Run(Func<Task> work)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task BackupCore(CancellationToken cancellationToken)
    {
        var capture = _panels.Capture();
        if (capture is null)
        {
            return;
        }

        var sources = Existing(capture.Sources);
        if (sources.Count == 0)
        {
            await None(_text.Get("cmd.backup")).ConfigureAwait(false);
            return;
        }

        if (await Refused(_text.Get("cmd.backup"), capture.OtherDirectory).ConfigureAwait(false))
        {
            return;
        }

        var job = _archives.Backup(
            capture.OtherDirectory,
            sources,
            capture.ActiveDirectory,
            DateTimeOffset.Now,
            cancellationToken);
        await FinishJob(job.Job).ConfigureAwait(false);
    }

    private static List<string> Existing(IReadOnlyList<string> paths)
    {
        var found = new List<string>();
        foreach (var path in paths)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                found.Add(path);
            }
        }

        return found;
    }

    /// <summary>Shows the read-only line and answers true when <paramref name="folder"/> is Computer or under it.</summary>
    private async Task<bool> Refused(string title, string? folder)
    {
        if (!FileOpGuard.IsTargetRefused(folder))
        {
            return false;
        }

        await _dialogs.ShowMessageAsync(title, _text.Get("computer.readonly")).ConfigureAwait(false);
        return true;
    }

    private Task None(string title) => _dialogs.ShowMessageAsync(title, _text.Get("dlg.no_selection"));

    private async Task FinishJob(IJob job)
    {
        var result = await job.Completion.ConfigureAwait(false);
        await _panels.RefreshAsync().ConfigureAwait(false);
        if (result.Outcome == JobOutcome.Cancelled || result.Failed.Count == 0)
        {
            return;
        }

        var lines = new List<string>();
        foreach (var item in result.Failed)
        {
            lines.Add(ErrorLines.Describe(_text, item.Error, item.Item));
        }

        await _dialogs.ShowMessageAsync(_text.Get("error"), string.Join('\n', lines)).ConfigureAwait(false);
    }
}
