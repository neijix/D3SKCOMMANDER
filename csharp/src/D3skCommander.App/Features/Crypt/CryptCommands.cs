using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.Common;
using D3skCommander.Core.Crypt;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Crypt;

/// <summary>Encrypt, Decrypt (dialog, then a job on Core's file encryption) and the Passwords window.</summary>
public sealed class CryptCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly IPasswordVault _vault;
    private readonly IToolWindowHost _tools;
    private readonly EncryptionCost? _cost;
    private int _busy;

    public CryptCommands(
        IPanelWorkspace panels,
        IDialogService dialogs,
        ILocalizer text,
        IJobService jobs,
        IPasswordVault vault,
        IToolWindowHost tools,
        CryptOptions options)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(options);
        _panels = panels;
        _dialogs = dialogs;
        _text = text;
        _jobs = jobs;
        _vault = vault;
        _tools = tools;
        _cost = options.Cost;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Encrypt, () => _ = EncryptAsync());
        shell.Handle(CommandId.Decrypt, () => _ = DecryptAsync());
        shell.Handle(CommandId.Passwords, () => _ = PasswordsAsync());
    }

    public Task EncryptAsync() => Run(() => Transform(true));

    public Task DecryptAsync() => Run(() => Transform(false));

    /// <summary>Unlocks the vault (asking the master once per session), then opens the manager.</summary>
    public async Task PasswordsAsync()
    {
        if (await _vault.UnlockAsync() is null)
        {
            return;
        }

        _tools.Open(ToolKind.Passwords);
    }

    private async Task Run(Func<Task> work)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await work();
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task Transform(bool encrypt)
    {
        var title = _text.Get(encrypt ? "cmd.encrypt" : "cmd.decrypt");
        var capture = _panels.Capture();
        if (capture is null)
        {
            return;
        }

        var files = Files(capture);
        if (files.Count == 0)
        {
            await _dialogs.ShowMessageAsync(title, _text.Get("dlg.no_selection"));
            return;
        }

        var dest = new FolderDialogField(_text.Get("target"), capture.OtherDirectory);
        var secret = new TextDialogField(_text.Get("password"), "", password: true);
        var again = new TextDialogField(_text.Get("confirm_password"), "", password: true);
        var fields = new List<DialogField> { new NoteDialogField(FilesLine(files)), dest, secret };
        if (encrypt)
        {
            fields.Add(again);
        }

        var form = new DialogForm(title, fields, f => Ready(dest, secret, again, encrypt));
        if (!await _dialogs.ShowFormAsync(form))
        {
            return;
        }

        if (!Ready(dest, secret, again, encrypt))
        {
            await _dialogs.ShowMessageAsync(_text.Get("error"), _text.Get(encrypt ? "crypt.mismatch" : "dlg.no_selection"));
            return;
        }

        var folder = dest.Path.Trim();
        if (FileOpGuard.IsTargetRefused(folder))
        {
            await _dialogs.ShowMessageAsync(title, _text.Get("computer.readonly"));
            return;
        }

        var pairs = Pairs(files, folder, encrypt);
        if (HasExisting(pairs) && !await _dialogs.ConfirmAsync(title, _text.Get("crypt.overwrite")))
        {
            return;
        }

        var job = Start(title, pairs, secret.Text, encrypt);
        await job.Completion;
        await _panels.RefreshAsync();
        await ShowFailures(job);
    }

    private IJob Start(string title, IReadOnlyList<KeyValuePair<string, string>> pairs, string password, bool encrypt)
    {
        var items = new List<JobItem>(pairs.Count);
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            items.Add(new JobItem(pair.Key, new FileInfo(pair.Key).Length));
            byName[pair.Key] = pair.Value;
        }

        return _jobs.Start(title, items, (item, progress, token) =>
        {
            var source = item.Name;
            var destination = byName[source];
            Func<long, bool> report = done =>
            {
                var current = progress.Current;
                progress.Report(new JobProgress(current.ItemsDone, current.ItemsTotal, done, current.BytesTotal, source));
                return !token.IsCancellationRequested;
            };
            var status = encrypt
                ? FileEncryption.EncryptFile(source, destination, password, report, _cost)
                : FileEncryption.DecryptFile(source, destination, password, report);
            return Task.FromResult(Result(status, token));
        });
    }

    private static ItemResult Result(FileStatus status, CancellationToken token)
    {
        if (status.IsSuccess)
        {
            return ItemResult.Success;
        }

        var error = status.Error!;
        if (error.Kind == ErrorKind.Cancelled)
        {
            throw new OperationCanceledException(token);
        }

        return ItemResult.Failed(error, retryable: false);
    }

    private async Task ShowFailures(IJob job)
    {
        var result = await job.Completion;
        if (result.Outcome == JobOutcome.Cancelled || result.Failed.Count == 0)
        {
            return;
        }

        var lines = new List<string>(result.Failed.Count);
        foreach (var item in result.Failed)
        {
            lines.Add(CryptErrors.Describe(_text, item.Error));
        }

        await _dialogs.ShowMessageAsync(_text.Get("error"), string.Join('\n', lines));
    }

    private static List<string> Files(PanelCapture capture)
    {
        var files = new List<string>();
        foreach (var path in capture.Sources)
        {
            if (File.Exists(path))
            {
                files.Add(path);
            }
        }

        return files;
    }

    private static List<KeyValuePair<string, string>> Pairs(IReadOnlyList<string> files, string folder, bool encrypt)
    {
        var pairs = new List<KeyValuePair<string, string>>(files.Count);
        foreach (var file in files)
        {
            var name = encrypt
                ? FileEncryption.DefaultEncryptedPath(Path.GetFileName(file))
                : FileEncryption.DefaultDecryptedPath(Path.GetFileName(file));
            pairs.Add(new KeyValuePair<string, string>(file, Path.Combine(folder, name)));
        }

        return pairs;
    }

    private static bool HasExisting(List<KeyValuePair<string, string>> pairs)
    {
        foreach (var pair in pairs)
        {
            if (File.Exists(pair.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Ready(FolderDialogField dest, TextDialogField secret, TextDialogField again, bool encrypt) =>
        dest.Path.Trim().Length > 0 && secret.Text.Length > 0 && (!encrypt || secret.Text == again.Text);

    private string FilesLine(IReadOnlyList<string> files) =>
        files.Count == 1 ? files[0] : files.Count + " " + _text.Get("files");
}
