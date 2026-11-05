using D3skCommander.App.Dialogs;
using D3skCommander.Core.Common;
using D3skCommander.Core.Crypt;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Crypt;

/// <summary>Opens the vault file with a master password asked through the dialog service, and keeps it for the session.</summary>
public sealed class PasswordVault : IPasswordVault
{
    private readonly string _path;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly EncryptionCost? _cost;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PasswordStore? _store;

    public PasswordVault(string path, IDialogService dialogs, ILocalizer text, CryptOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        _path = path;
        _dialogs = dialogs;
        _text = text;
        _cost = options.Cost;
    }

    public PasswordStore? Current => Volatile.Read(ref _store);

    public async Task<PasswordStore?> UnlockAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var open = Current;
            if (open is not null)
            {
                return open;
            }

            var opened = await OpenAsync(File.Exists(_path));
            Volatile.Write(ref _store, opened);
            return opened;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationError?> SaveAsync()
    {
        var store = Current;
        if (store is null)
        {
            return OperationError.AccessDenied("locked");
        }

        var status = await Task.Run(() => store.Save(_cost));
        return status.Error;
    }

    public void Lock() => Volatile.Write(ref _store, null);

    private async Task<PasswordStore?> OpenAsync(bool exists)
    {
        while (true)
        {
            var master = await AskMasterAsync(exists);
            if (master is null)
            {
                return null;
            }

            var opened = await Task.Run(() => PasswordStore.Open(_path, master));
            if (opened.IsSuccess && !exists)
            {
                var saved = await Task.Run(() => opened.Value.Save(_cost));
                if (!saved.IsSuccess)
                {
                    await Fail(saved.Error!);
                    return null;
                }
            }

            if (opened.IsSuccess)
            {
                return opened.Value;
            }

            await Fail(opened.Error!);
            if (opened.Error!.Kind != ErrorKind.AccessDenied)
            {
                return null;
            }
        }
    }

    private async Task<string?> AskMasterAsync(bool exists)
    {
        var master = new TextDialogField(_text.Get("master_password"), "", password: true);
        if (exists)
        {
            var unlock = new DialogForm(_text.Get("crypt.unlock_title"), new DialogField[] { master }, form => master.Text.Length > 0);
            return await _dialogs.ShowFormAsync(unlock) ? master.Text : null;
        }

        var confirm = new TextDialogField(_text.Get("confirm_password"), "", password: true);
        var fields = new DialogField[] { new NoteDialogField(_text.Get("crypt.create_note")), master, confirm };
        var create = new DialogForm(
            _text.Get("crypt.create_title"),
            fields,
            form => master.Text.Length > 0 && master.Text == confirm.Text);
        while (await _dialogs.ShowFormAsync(create))
        {
            if (master.Text.Length > 0 && master.Text == confirm.Text)
            {
                return master.Text;
            }

            await _dialogs.ShowMessageAsync(_text.Get("error"), _text.Get("crypt.mismatch"));
        }

        return null;
    }

    private Task Fail(OperationError error) =>
        _dialogs.ShowMessageAsync(_text.Get("error"), CryptErrors.Describe(_text, error));
}
