using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.Core.Associations;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Settings;

/// <summary>
/// The "File associations" section of the settings window: a draft of the rules. Add, remove and move
/// a rule; edit its name, masks, the Open, View and Edit actions and the extra actions.
/// </summary>
public sealed partial class AssociationsEditorModel : ObservableObject
{
    private readonly ILocalizer _text;

    [ObservableProperty]
    private AssociationRuleRow? _selectedRow;

    public AssociationsEditorModel(ILocalizer text, IEnumerable<AssociationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(rules);
        _text = text;
        foreach (var rule in rules)
        {
            var row = new AssociationRuleRow(rule, text.Get("assoc.extra_new"));
            row.Changed += OnRowChanged;
            Rows.Add(row);
        }

        SelectedRow = Rows.FirstOrDefault();
        Rows.CollectionChanged += OnRowsChanged;
    }

    /// <summary>Raised when the draft changed: a rule added, removed or moved, or any field of a rule edited.</summary>
    public event EventHandler? DraftChanged;

    public ObservableCollection<AssociationRuleRow> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    public string Title => _text.Get("assoc.title");

    public string AddCaption => _text.Get("assoc.add");

    public string RemoveCaption => _text.Get("assoc.remove");

    public string UpCaption => _text.Get("assoc.up");

    public string DownCaption => _text.Get("assoc.down");

    public string EmptyText => _text.Get("assoc.empty");

    public string NameCaption => _text.Get("assoc.name");

    public string MasksCaption => _text.Get("assoc.masks");

    public string ActionCaption => _text.Get("assoc.action");

    public string ProgramCaption => _text.Get("assoc.program");

    public string ArgumentsCaption => _text.Get("assoc.arguments");

    public string FolderCaption => _text.Get("assoc.folder");

    public string OpenCaption => _text.Get("assoc.open");

    public string ViewCaption => _text.Get("assoc.view");

    public string EditCaption => _text.Get("assoc.edit");

    public string ExtrasCaption => _text.Get("assoc.extras");

    public string AddExtraCaption => _text.Get("assoc.extra_add");

    public string RemoveExtraCaption => _text.Get("assoc.extra_remove");

    public string Hint => _text.Get("assoc.hint");

    /// <summary>The rules of the draft, in order, without those that have no mask: what Apply stores.</summary>
    public IReadOnlyList<AssociationRule> Result() => [.. Rows.Select(row => row.ToRule()).OfType<AssociationRule>()];

    /// <summary>Adds an empty rule after the selected one (else at the end) and selects it.</summary>
    public AssociationRuleRow AddRule(string name = "", string masks = "")
    {
        var row = new AssociationRuleRow(null, _text.Get("assoc.extra_new")) { Name = name.Length == 0 ? _text.Get("assoc.new_rule") : name, Masks = masks };
        Rows.Insert(SelectedRow is null ? Rows.Count : Rows.IndexOf(SelectedRow) + 1, row);
        SelectedRow = row;
        return row;
    }

    [RelayCommand]
    public void Add() => AddRule();

    [RelayCommand]
    public void Remove()
    {
        if (SelectedRow is null)
        {
            return;
        }

        var index = Rows.IndexOf(SelectedRow);
        Rows.RemoveAt(index);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
    }

    [RelayCommand]
    public void MoveUp() => Move(-1);

    [RelayCommand]
    public void MoveDown() => Move(1);

    private void OnRowChanged(object? sender, EventArgs e) => DraftChanged?.Invoke(this, EventArgs.Empty);

    private void OnRowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmpty));
        // Move lists the same row in both. Detach first so the re-attach is what remains.
        foreach (var removed in e.OldItems?.OfType<AssociationRuleRow>() ?? [])
        {
            removed.Changed -= OnRowChanged;
        }

        foreach (var added in e.NewItems?.OfType<AssociationRuleRow>() ?? [])
        {
            added.Changed += OnRowChanged;
        }

        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Move(int delta)
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        var from = Rows.IndexOf(row);
        var to = from + delta;
        if (to >= 0 && to < Rows.Count)
        {
            Rows.Move(from, to);
            SelectedRow = row;
        }
    }
}
