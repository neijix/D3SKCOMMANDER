using System.Globalization;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;

namespace D3skCommander.App.Features.FileOps;

/// <summary>F5, F6, F7 and F8. Work runs on the job service; the panels refresh when it ends.</summary>
public sealed class FileOpsCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly FileOperationService _files;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly IFileSystemProvider _provider;
    private int _busy;

    public FileOpsCommands(
        IPanelWorkspace panels,
        FileOperationService files,
        IDialogService dialogs,
        ILocalizer text,
        IJobService jobs,
        IFileSystemProvider provider)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(provider);
        _panels = panels;
        _files = files;
        _dialogs = dialogs;
        _text = text;
        _jobs = jobs;
        _provider = provider;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Copy, () => _ = CopyAsync());
        shell.Handle(CommandId.Move, () => _ = MoveAsync());
        shell.Handle(CommandId.MkDir, () => _ = NewFolderAsync());
        shell.Handle(CommandId.Delete, () => _ = DeleteAsync());
    }

    public Task CopyAsync() => TransferAsync(FileOpKind.Copy);

    public Task MoveAsync() => TransferAsync(FileOpKind.Move);

    public async Task DeleteAsync()
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            var capture = _panels.Capture();
            if (capture is null || capture.Sources.Count == 0)
            {
                await None(_text.Get("cmd.delete")).ConfigureAwait(false);
                return;
            }

            var message = Count(_text.Get("dlg.delete"), capture.Sources.Count);
            if (!await _dialogs.ConfirmAsync(_text.Get("cmd.delete"), message).ConfigureAwait(false))
            {
                return;
            }

            await Finish(_files.Delete(_provider, capture.Sources)).ConfigureAwait(false);
        }
        finally
        {
            End();
        }
    }

    public async Task NewFolderAsync()
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            var capture = _panels.Capture();
            if (capture is null)
            {
                return;
            }

            var name = await _dialogs.AskTextAsync(_text.Get("cmd.mkdir"), _text.Get("dlg.mkdir")).ConfigureAwait(false);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            await Finish(_files.NewFolder(_provider, capture.ActiveDirectory, name)).ConfigureAwait(false);
        }
        finally
        {
            End();
        }
    }

    private async Task TransferAsync(FileOpKind kind)
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            var title = _text.Get(kind == FileOpKind.Move ? "cmd.move" : "cmd.copy");
            var capture = _panels.Capture();
            if (capture is null || capture.Sources.Count == 0)
            {
                await None(title).ConfigureAwait(false);
                return;
            }

            var target = await AskTarget(kind, title, capture).ConfigureAwait(false);
            if (target is null)
            {
                return;
            }

            var directory = await Task.Run(() => Directory.Exists(target)).ConfigureAwait(false);
            var operation = _files.TransferToTarget(
                kind,
                _provider,
                capture.Sources,
                _provider,
                target,
                directory,
                AskConflict);
            await Finish(operation).ConfigureAwait(false);
        }
        finally
        {
            End();
        }
    }

    private async Task<string?> AskTarget(FileOpKind kind, string title, PanelCapture capture)
    {
        var labelKey = kind == FileOpKind.Move ? "dlg.move_to" : "dlg.copy_to";
        var label = Count(_text.Get(labelKey), capture.Sources.Count);
        var field = new FolderDialogField(label, capture.OtherDirectory);
        var form = new DialogForm(title, [field], candidate => Folder(candidate).Path.Trim().Length > 0);
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(false))
        {
            return null;
        }

        var path = Folder(form).Path.Trim();
        return path.Length == 0 ? null : path;
    }

    private async Task Finish(FileOperation operation)
    {
        await operation.Job.Completion.ConfigureAwait(false);
        await _panels.RefreshAsync().ConfigureAwait(false);
        if (operation.ErrorCount == 0)
        {
            return;
        }

        var choice = await _dialogs.ChooseAsync(
            _text.Get("jobs.failed"),
            string.Join('\n', operation.ErrorMessages()),
            [_text.Get("jobs.retry"), _text.Get("dlg.skip")]).ConfigureAwait(false);
        if (choice != 0)
        {
            return;
        }

        var names = new List<string>();
        foreach (var item in operation.Job.Completion.Result.Failed)
        {
            if (item.Retryable)
            {
                names.Add(item.Item);
            }
        }

        if (names.Count == 0)
        {
            return;
        }

        await _jobs.Retry(operation.Job, names).Completion.ConfigureAwait(false);
        await _panels.RefreshAsync().ConfigureAwait(false);
    }

    private Task<ConflictChoice> AskConflict(ConflictQuestion question, CancellationToken cancellationToken)
    {
        var body = _text.Get("dlg.source") + ": " + question.SourcePath + "\n"
            + _text.Get("dlg.existing") + ": " + question.TargetPath;
        var choices = new[]
        {
            _text.Get("dlg.overwrite_btn"),
            _text.Get("dlg.skip"),
            _text.Get("cancel"),
            _text.Get("dlg.overwrite_all"),
            _text.Get("dlg.skip_all"),
        };
        return Map(_dialogs.ChooseAsync(_text.Get("dlg.overwrite"), body, choices, cancellationToken));
    }

    private static async Task<ConflictChoice> Map(Task<int?> choice)
    {
        return (await choice.ConfigureAwait(false)) switch
        {
            0 => ConflictChoice.Overwrite,
            1 => ConflictChoice.Skip,
            3 => ConflictChoice.OverwriteAll,
            4 => ConflictChoice.SkipAll,
            _ => ConflictChoice.Cancel,
        };
    }

    private Task None(string title) => _dialogs.ShowMessageAsync(title, _text.Get("dlg.no_selection"));

    private bool Begin() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    private void End() => Interlocked.Exchange(ref _busy, 0);

    private static string Count(string template, int count) =>
        template.Replace("{n}", count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static FolderDialogField Folder(DialogForm form) => (FolderDialogField)form.Fields[0];
}
