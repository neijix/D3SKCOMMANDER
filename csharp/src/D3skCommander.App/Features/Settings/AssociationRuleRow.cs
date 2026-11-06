using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.Core.Associations;

namespace D3skCommander.App.Features.Settings;

/// <summary>One rule being edited: name, masks, the three actions and the extra actions.</summary>
public sealed partial class AssociationRuleRow : ObservableObject
{
    private readonly string _newExtraName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _masks = "";

    [ObservableProperty]
    private AssociationActionRow? _selectedExtra;

    public AssociationRuleRow(AssociationRule? rule, string newExtraName)
    {
        _newExtraName = newExtraName;
        Name = rule?.Name ?? "";
        Masks = rule?.Masks ?? "";
        Open = AssociationActionRow.From(rule?.Open);
        View = AssociationActionRow.From(rule?.View);
        Edit = AssociationActionRow.From(rule?.Edit);
        foreach (var extra in rule?.Extras ?? [])
        {
            Extras.Add(AssociationActionRow.From(extra));
        }

        SelectedExtra = Extras.FirstOrDefault();
    }

    public AssociationActionRow Open { get; }

    public AssociationActionRow View { get; }

    public AssociationActionRow Edit { get; }

    public ObservableCollection<AssociationActionRow> Extras { get; } = [];

    /// <summary>The list line: the name and the masks.</summary>
    public string Display => Name.Trim().Length == 0 ? Masks : Name + "  (" + Masks + ")";

    /// <summary>The rule, or null while no mask is typed (it would match nothing).</summary>
    public AssociationRule? ToRule() => Masks.Trim().Length == 0
        ? null
        : new AssociationRule(Name, Masks, Open.ToAction(), View.ToAction(), Edit.ToAction(), Extras.Select(extra => extra.ToAction()).OfType<AssociationAction>().Where(extra => extra.Name.Length > 0));

    [RelayCommand]
    public void AddExtra()
    {
        var extra = new AssociationActionRow { Name = _newExtraName };
        Extras.Add(extra);
        SelectedExtra = extra;
    }

    [RelayCommand]
    public void RemoveExtra()
    {
        if (SelectedExtra is null)
        {
            return;
        }

        var index = Extras.IndexOf(SelectedExtra);
        Extras.RemoveAt(index);
        SelectedExtra = Extras.Count == 0 ? null : Extras[Math.Min(index, Extras.Count - 1)];
    }
}
