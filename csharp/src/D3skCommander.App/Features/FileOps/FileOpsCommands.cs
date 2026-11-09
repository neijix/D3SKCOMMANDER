using System.Globalization;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;

namespace D3skCommander.App.Features.FileOps;

/// <summary>F5, F6, F7, F8, Shift+F8 and F2. Work runs on the job service; the panels refresh when it ends.</summary>
public sealed partial class FileOpsCommands : IPathDeleter
{
    private readonly IPanelWorkspace _panels;
    private readonly FileOperationService _files;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly IFileSystemProvider _provider;
    private readonly AppSettings _settings;
    private readonly ITrash _trash;
    private int _busy;

    public FileOpsCommands(
        IPanelWorkspace panels,
        FileOperationService files,
        IDialogService dialogs,
        ILocalizer text,
        IJobService jobs,
        IFileSystemProvider provider,
        AppSettings settings,
        ITrash trash)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(trash);
        _panels = panels;
        _files = files;
        _dialogs = dialogs;
        _text = text;
        _jobs = jobs;
        _provider = provider;
        _settings = settings;
        _trash = trash;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Copy, () => _ = CopyAsync());
        shell.Handle(CommandId.Move, () => _ = MoveAsync());
        shell.Handle(CommandId.ClipCopy, () => _ = ClipAsync(cut: false));
        shell.Handle(CommandId.ClipCut, () => _ = ClipAsync(cut: true));
        shell.Handle(CommandId.ClipPaste, () => _ = PasteAsync());
        shell.Handle(CommandId.MkDir, () => _ = NewFolderAsync());
        shell.Handle(CommandId.Delete, () => _ = DeleteAsync());
        shell.Handle(CommandId.DeletePermanent, () => _ = DeleteAsync(CommandId.DeletePermanent));
        shell.Handle(CommandId.RenameInPlace, BeginRename);
        if (_panels is PanelWorkspace workspace)
        {
            workspace.ClipboardHasFiles = () => !Clipboard.IsEmpty;
        }

        if (_panels is IInPlaceRename rename)
        {
            rename.Commit = RenameAsync;
        }
    }

    public Task CopyAsync() => TransferAsync(FileOpKind.Copy);

    public Task MoveAsync() => TransferAsync(FileOpKind.Move);

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

            if (await Refused(GuardedOp.NewFolder, capture.ActiveDirectory, null, _text.Get("cmd.mkdir")).ConfigureAwait(false))
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
            var op = kind == FileOpKind.Move ? GuardedOp.Move : GuardedOp.Copy;
            if (capture is not null && await Refused(op, capture.ActiveDirectory, null, title).ConfigureAwait(false))
            {
                return;
            }

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

            if (await Refused(op, capture.ActiveDirectory, target, title).ConfigureAwait(false))
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

    private Task Finish(FileOperation operation) =>
        Finish(operation.Job, () => operation.ErrorCount, () => operation.ErrorMessages());

    private async Task Finish(IJob job, Func<int> errorCount, Func<IReadOnlyList<string>> lines)
    {
        await job.Completion.ConfigureAwait(false);
        await _panels.RefreshAsync().ConfigureAwait(false);
        if (errorCount() == 0)
        {
            return;
        }

        var choice = await _dialogs.ChooseAsync(
            _text.Get("jobs.failed"),
            string.Join('\n', lines()),
            [_text.Get("jobs.retry"), _text.Get("dlg.skip")]).ConfigureAwait(false);
        if (choice != 0)
        {
            return;
        }

        var names = new List<string>();
        foreach (var item in job.Completion.Result.Failed)
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

        await _jobs.Retry(job, names).Completion.ConfigureAwait(false);
        await _panels.RefreshAsync().ConfigureAwait(false);
    }

    private async Task<ConflictChoice> AskConflict(ConflictQuestion question, CancellationToken cancellationToken)
    {
        var choice = await _dialogs.ChooseAsync(
            _text.Get("dlg.overwrite"),
            ConflictPrompt.Body(_text, question),
            ConflictPrompt.Choices(_text),
            cancellationToken).ConfigureAwait(false);
        return ConflictPrompt.Map(choice);
    }

    /// <summary>Shows the read-only line and answers true when Computer refuses <paramref name="op"/>.</summary>
    private async Task<bool> Refused(GuardedOp op, string active, string? target, string title)
    {
        if (!FileOpGuard.IsRefused(op, active, target))
        {
            return false;
        }

        await _dialogs.ShowMessageAsync(title, _text.Get("computer.readonly")).ConfigureAwait(false);
        return true;
    }

    private Task None(string title) => _dialogs.ShowMessageAsync(title, _text.Get("dlg.no_selection"));

    private bool Begin() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    private void End() => Interlocked.Exchange(ref _busy, 0);

    internal static string Count(string template, int count) =>
        template.Replace("{n}", count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static FolderDialogField Folder(DialogForm form) => (FolderDialogField)form.Fields[0];
}
