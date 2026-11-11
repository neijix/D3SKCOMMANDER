using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Dialogs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Settings;

/// <summary>One saved set in the list: its name and how many tabs each side holds.</summary>
public sealed record FavoriteRow(string Name, string Summary)
{
    public string Display => Name + "  (" + Summary + ")";
}

/// <summary>
/// The "Favorite tabs" section of the settings window: a draft of the saved sets (rename, delete, move up
/// and down) and the choice of what opening a set does with the open tabs.
/// </summary>
public sealed partial class FavoriteTabsEditorModel : ObservableObject
{
    private readonly ILocalizer _text;
    private readonly IDialogService _dialogs;
    private readonly FavoriteTabs _draft;

    [ObservableProperty]
    private FavoriteRow? _selectedRow;

    [ObservableProperty]
    private int _modeIndex;

    public FavoriteTabsEditorModel(ILocalizer text, IDialogService dialogs, FavoriteTabs saved, FavoriteRestoreMode mode)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(saved);
        _text = text;
        _dialogs = dialogs;
        _draft = saved.Clone();
        ModeIndex = mode == FavoriteRestoreMode.Add ? 1 : 0;
        Refresh(null);
    }

    /// <summary>Raised when the draft changed: a set renamed, deleted or moved, or the restore mode changed.</summary>
    public event EventHandler? DraftChanged;

    public ObservableCollection<FavoriteRow> Rows { get; } = [];

    /// <summary>True once a set was renamed, deleted or moved: only then does Apply write the file.</summary>
    public bool Changed { get; private set; }

    public string Title => _text.Get("favorites.title");

    public string ModeCaption => _text.Get("favorites.mode");

    public IReadOnlyList<string> Modes => [_text.Get("favorites.mode_replace"), _text.Get("favorites.mode_add")];

    public string RenameCaption => _text.Get("favorites.rename");

    public string DeleteCaption => _text.Get("favorites.delete");

    public string UpCaption => _text.Get("favorites.up");

    public string DownCaption => _text.Get("favorites.down");

    public string EmptyText => _text.Get("favorites.empty");

    public bool IsEmpty => Rows.Count == 0;

    public FavoriteRestoreMode Mode => ModeIndex == 1 ? FavoriteRestoreMode.Add : FavoriteRestoreMode.Replace;

    /// <summary>The sets the draft holds now.</summary>
    public FavoriteTabs Result() => _draft.Clone();

    [RelayCommand]
    public async Task RenameAsync()
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        var name = await _dialogs.AskTextAsync(Title, _text.Get("favorites.rename_prompt"), row.Name).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(name))
        {
            Rename(row.Name, name);
        }
    }

    /// <summary>Renames a set; a blank name or one another set has is refused.</summary>
    public bool Rename(string name, string newName)
    {
        if (!_draft.Rename(name, newName))
        {
            return false;
        }

        Changed = true;
        Refresh(newName.Trim());
        RaiseDraftChanged();
        return true;
    }

    [RelayCommand]
    public void Delete()
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        _draft.Delete(row.Name);
        Changed = true;
        Refresh(Rows.Count > 1 ? Rows[Math.Clamp(index == Rows.Count - 1 ? index - 1 : index + 1, 0, Rows.Count - 1)].Name : null);
        RaiseDraftChanged();
    }

    [RelayCommand]
    public void MoveUp() => Move(-1);

    [RelayCommand]
    public void MoveDown() => Move(1);

    private void Move(int delta)
    {
        var row = SelectedRow;
        if (row is not null && _draft.Move(row.Name, delta))
        {
            Changed = true;
            Refresh(row.Name);
            RaiseDraftChanged();
        }
    }

    partial void OnModeIndexChanged(int value) => RaiseDraftChanged();

    private void RaiseDraftChanged() => DraftChanged?.Invoke(this, EventArgs.Empty);

    private void Refresh(string? select)
    {
        Rows.Clear();
        foreach (var set in _draft.Sets)
        {
            Rows.Add(new FavoriteRow(set.Name, Summary(set)));
        }

        SelectedRow = select is null ? null : Rows.FirstOrDefault(row => string.Equals(row.Name, select, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private string Summary(FavoriteTabSet set) => _text.Get("favorites.summary")
        .Replace("{left}", set.Left.Tabs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{right}", set.Right.Tabs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
