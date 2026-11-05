using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Crypt;

/// <summary>The password manager: the vault's names, and one entry edited at a time. Secrets are masked until shown.</summary>
public sealed partial class PasswordsViewModel : ObservableObject, IShellWidget
{
    private readonly IPasswordVault _vault;
    private readonly ILocalizer _text;

    [ObservableProperty]
    private string? _selected;

    [ObservableProperty]
    private string _entryName = "";

    [ObservableProperty]
    private string _secret = "";

    [ObservableProperty]
    private bool _isRevealed;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isUnlocked;

    public PasswordsViewModel(IPasswordVault vault, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(text);
        _vault = vault;
        _text = text;
        Reload();
    }

    /// <summary>Raised with the secret to place on the clipboard. The view owns the clipboard.</summary>
    public event Action<string>? CopyRequested;

    public string? Name => "Passwords";

    public ObservableCollection<string> Keys { get; } = [];

    public char SecretChar => IsRevealed ? '\0' : '•';

    public string RevealText => _text.Get(IsRevealed ? "pw.hide" : "pw.show");

    public string NameLabel => _text.Get("pw.name");

    public string SecretLabel => _text.Get("pw.secret");

    public string NewText => _text.Get("pw.new");

    public string SaveText => _text.Get("save");

    public string RemoveText => _text.Get("pw.remove");

    public string CopyText => _text.Get("pw.copy");

    public string LockText => _text.Get("pw.lock");

    public string EmptyText => _text.Get("pw.empty");

    public bool IsEmpty => IsUnlocked && Keys.Count == 0;

    public bool CanSave => IsUnlocked && EntryName.Trim().Length > 0;

    public bool CanUseEntry => IsUnlocked && Selected is not null;

    public void Reload()
    {
        var store = _vault.Current;
        IsUnlocked = store is not null;
        Keys.Clear();
        if (store is not null)
        {
            foreach (var key in store.Keys())
            {
                Keys.Add(key);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanUseEntry));
    }

    partial void OnSelectedChanged(string? value)
    {
        OnPropertyChanged(nameof(CanUseEntry));
        if (value is null)
        {
            return;
        }

        EntryName = value;
        Secret = _vault.Current?.Get(value) ?? "";
        IsRevealed = false;
    }

    partial void OnEntryNameChanged(string value) => OnPropertyChanged(nameof(CanSave));

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(SecretChar));
        OnPropertyChanged(nameof(RevealText));
    }

    [RelayCommand]
    private void NewEntry()
    {
        Selected = null;
        EntryName = "";
        Secret = "";
        IsRevealed = false;
        Status = "";
    }

    [RelayCommand]
    private void Reveal() => IsRevealed = !IsRevealed;

    [RelayCommand]
    private void Copy()
    {
        if (Selected is null || _vault.Current?.Get(Selected) is not { } secret)
        {
            return;
        }

        CopyRequested?.Invoke(secret);
        Status = _text.Get("pw.copied");
    }

    [RelayCommand]
    public async Task SaveEntryAsync()
    {
        var store = _vault.Current;
        var name = EntryName.Trim();
        if (store is null || name.Length == 0)
        {
            return;
        }

        store.Set(name, Secret);
        await PersistAsync("pw.saved");
        Reload();
        Selected = name;
    }

    [RelayCommand]
    public async Task RemoveEntryAsync()
    {
        var store = _vault.Current;
        var name = Selected;
        if (store is null || name is null)
        {
            return;
        }

        store.Remove(name);
        await PersistAsync("pw.removed");
        Reload();
        NewEntry();
    }

    [RelayCommand]
    private void LockVault()
    {
        _vault.Lock();
        NewEntry();
        Reload();
        Status = _text.Get("pw.locked");
    }

    private async Task PersistAsync(string doneKey)
    {
        var error = await _vault.SaveAsync();
        Status = error is null ? _text.Get(doneKey) : CryptErrors.Describe(_text, error);
    }

    public Control CreateContent() => new PasswordsView(this);
}
