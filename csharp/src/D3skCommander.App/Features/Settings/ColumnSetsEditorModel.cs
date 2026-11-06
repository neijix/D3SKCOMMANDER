using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Dialogs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Settings;

/// <summary>
/// The "Columns" section of the settings window: a draft of the column sets (add, rename, delete) and,
/// for the selected one, its columns with a checkbox, an order and a width.
/// </summary>
public sealed partial class ColumnSetsEditorModel : ObservableObject
{
    private readonly ILocalizer _text;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private ColumnSet? _selectedSet;

    [ObservableProperty]
    private ColumnRow? _selectedRow;

    public ColumnSetsEditorModel(ILocalizer text, IDialogService dialogs, IEnumerable<ColumnSet> sets)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(sets);
        _text = text;
        _dialogs = dialogs;
        Load(sets);
    }

    public ObservableCollection<ColumnSet> Sets { get; } = [];

    public ObservableCollection<ColumnRow> Rows { get; } = [];

    public string Title => _text.Get("columnsets.title");

    public string ColumnsCaption => _text.Get("columnsets.columns");

    public string WidthCaption => _text.Get("columnsets.width");

    public string AddCaption => _text.Get("columnsets.add");

    public string RenameCaption => _text.Get("columnsets.rename");

    public string DeleteCaption => _text.Get("columnsets.delete");

    public string UpCaption => _text.Get("columnsets.up");

    public string DownCaption => _text.Get("columnsets.down");

    public string BuiltInNote => _text.Get("columnsets.builtin");

    public bool IsCustomSelected => SelectedSet is not null && !ColumnSetCatalog.IsBuiltIn(SelectedSet.Name);

    /// <summary>The sets the draft holds now, the selected one included.</summary>
    public IReadOnlyList<ColumnSet> Result()
    {
        Commit(SelectedSet);
        return [.. Sets.Select(set => set.Clone())];
    }

    /// <summary>Adds an empty set called <paramref name="name"/> (made unique) and selects it.</summary>
    public ColumnSet Add(string name)
    {
        var unique = ColumnSetCatalog.UniqueName(Sets.Select(set => set.Name), name);
        var made = new ColumnSet(unique, [new ColumnSetColumn(ColumnId.Extension), new ColumnSetColumn(ColumnId.Size), new ColumnSetColumn(ColumnId.Modified)]);
        Sets.Add(made);
        SelectedSet = made;
        return made;
    }

    [RelayCommand]
    public async Task AddSetAsync()
    {
        var name = await AskNameAsync(_text.Get("columnsets.new")).ConfigureAwait(true);
        if (name is not null)
        {
            Add(name);
        }
    }

    [RelayCommand]
    public async Task RenameSetAsync()
    {
        var set = SelectedSet;
        if (set is null || ColumnSetCatalog.IsBuiltIn(set.Name))
        {
            return;
        }

        var name = await AskNameAsync(set.Name).ConfigureAwait(true);
        if (name is not null)
        {
            Rename(set, name);
        }
    }

    [RelayCommand]
    public void DeleteSet()
    {
        var set = SelectedSet;
        if (set is null || ColumnSetCatalog.IsBuiltIn(set.Name))
        {
            return;
        }

        var index = Sets.IndexOf(set);
        Sets.Remove(set);
        SelectedSet = Sets[Math.Clamp(index - 1, 0, Sets.Count - 1)];
    }

    [RelayCommand]
    public void MoveUp() => Move(-1);

    [RelayCommand]
    public void MoveDown() => Move(1);

    partial void OnSelectedSetChanging(ColumnSet? oldValue, ColumnSet? newValue) => Commit(oldValue);

    partial void OnSelectedSetChanged(ColumnSet? value)
    {
        Rows.Clear();
        OnPropertyChanged(nameof(IsCustomSelected));
        if (value is null)
        {
            return;
        }

        foreach (var column in value.Columns)
        {
            Rows.Add(RowOf(column.Id, true, column.Width));
        }

        foreach (var id in ColumnInfo.Optional.Where(id => !value.Has(id)))
        {
            Rows.Add(RowOf(id, false, 0));
        }
    }

    private void Rename(ColumnSet set, string name)
    {
        var others = Sets.Where(other => !ReferenceEquals(other, set)).Select(other => other.Name);
        var index = Sets.IndexOf(set);
        Commit(set);
        var renamed = new ColumnSet(ColumnSetCatalog.UniqueName(others, name), set.Columns);
        Sets[index] = renamed;
        SelectedSet = renamed;
    }

    private ColumnRow RowOf(ColumnId id, bool shown, double width) =>
        new(id, _text.Get(ColumnInfo.HeaderKey(id)), shown, width);

    private void Move(int delta)
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        var from = Rows.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Rows.Count)
        {
            return;
        }

        Rows.Move(from, to);
        SelectedRow = row;
    }

    // Writes the rows back into the set they were made from: the shown ones, in row order.
    private void Commit(ColumnSet? set)
    {
        if (set is null || Rows.Count == 0)
        {
            return;
        }

        var columns = Rows.Where(row => row.IsShown)
            .Select(row => new ColumnSetColumn(row.Id, (double)(row.Width ?? 0)));
        set.Columns = [.. new ColumnSet(set.Name, columns).Normalized().Columns];
    }

    private async Task<string?> AskNameAsync(string initial)
    {
        var field = new TextDialogField(_text.Get("columnsets.name_prompt"), initial);
        var form = new DialogForm(_text.Get("columnsets.title"), [field], _ => field.Text.Trim().Length > 0);
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            return null;
        }

        return field.Text.Trim();
    }

    private void Load(IEnumerable<ColumnSet> sets)
    {
        foreach (var set in sets)
        {
            Sets.Add(set.Clone());
        }

        SelectedSet = Sets.FirstOrDefault();
    }
}
