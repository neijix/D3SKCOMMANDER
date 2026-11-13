using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Dialogs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Settings;

namespace D3skCommander.App.Features.Settings;

/// <summary>One favorite in the list: its name and the folder or file it opens.</summary>
public sealed record HotlistRow(string Name, string Path)
{
    public string Display => Name + "  —  " + Path;
}

/// <summary>The "Favorites" section of the settings window: a draft of the hotlist to rename, remove and reorder.</summary>
public sealed partial class HotlistEditorModel : ObservableObject
{
    private readonly ILocalizer _text;
    private readonly IDialogService _dialogs;
    private readonly Hotlist _draft;

    [ObservableProperty]
    private HotlistRow? _selectedRow;

    public HotlistEditorModel(ILocalizer text, IDialogService dialogs, Hotlist saved)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(saved);
        _text = text;
        _dialogs = dialogs;
        _draft = saved.Clone();
        Refresh(-1);
    }

    /// <summary>Raised when a favorite was renamed, removed or moved.</summary>
    public event EventHandler? DraftChanged;

    public ObservableCollection<HotlistRow> Rows { get; } = [];

    public string Title => _text.Get("hotlist.title");

    public string RenameCaption => _text.Get("favorites.rename");

    public string RemoveCaption => _text.Get("hotlist.remove");

    public string UpCaption => _text.Get("favorites.up");

    public string DownCaption => _text.Get("favorites.down");

    public string EmptyText => _text.Get("hotlist.empty");

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The favorites the draft holds now.</summary>
    public Hotlist Result() => _draft.Clone();

    [RelayCommand]
    public async Task RenameAsync()
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        var name = await _dialogs.AskTextAsync(Title, _text.Get("favorites.rename_prompt"), row.Name).ConfigureAwait(true);
        if (name is not null)
        {
            Rename(Rows.IndexOf(row), name);
        }
    }

    /// <summary>Renames the favorite at <paramref name="index"/>; a blank name is refused.</summary>
    public bool Rename(int index, string name)
    {
        if (!_draft.Rename(index, name))
        {
            return false;
        }

        Changed(index);
        return true;
    }

    [RelayCommand]
    public void Remove()
    {
        var index = SelectedRow is null ? -1 : Rows.IndexOf(SelectedRow);
        if (_draft.RemoveAt(index))
        {
            Changed(Math.Min(index, _draft.Items.Count - 1));
        }
    }

    [RelayCommand]
    public void MoveUp() => Move(-1);

    [RelayCommand]
    public void MoveDown() => Move(1);

    private void Move(int delta)
    {
        var index = SelectedRow is null ? -1 : Rows.IndexOf(SelectedRow);
        if (_draft.Move(index, delta))
        {
            Changed(index + delta);
        }
    }

    private void Changed(int select)
    {
        Refresh(select);
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh(int select)
    {
        Rows.Clear();
        foreach (var item in _draft.Items)
        {
            Rows.Add(new HotlistRow(item.Name, item.Path));
        }

        SelectedRow = select >= 0 && select < Rows.Count ? Rows[select] : null;
        OnPropertyChanged(nameof(IsEmpty));
    }
}
