using D3skCommander.Core.Wildcard;

namespace D3skCommander.Core.Associations;

/// <summary>
/// One file association: <see cref="Masks"/> (';'-separated, case-insensitive, matched against the file
/// name by <see cref="Mask"/>), the optional Open, View and Edit actions and any number of named
/// <see cref="Extras"/> for the context menu. A rule without masks matches nothing.
/// </summary>
public sealed class AssociationRule
{
    private readonly Lazy<Mask?> _mask;

    public AssociationRule(
        string name,
        string masks,
        AssociationAction? open = null,
        AssociationAction? view = null,
        AssociationAction? edit = null,
        IEnumerable<AssociationAction>? extras = null)
    {
        Masks = (masks ?? "").Trim();
        Name = string.IsNullOrWhiteSpace(name) ? Masks : name.Trim();
        Open = Clean(open);
        View = Clean(view);
        Edit = Clean(edit);
        Extras = [.. (extras ?? []).Where(extra => extra is not null && extra.Name.Trim().Length > 0 && extra.Program.Trim().Length > 0)];
        _mask = new Lazy<Mask?>(() => Masks.Length == 0 ? null : new Mask(Masks));
    }

    public string Name { get; }

    public string Masks { get; }

    public AssociationAction? Open { get; }

    public AssociationAction? View { get; }

    public AssociationAction? Edit { get; }

    public IReadOnlyList<AssociationAction> Extras { get; }

    public AssociationAction? Action(AssociationKind kind) => kind switch
    {
        AssociationKind.Open => Open,
        AssociationKind.View => View,
        AssociationKind.Edit => Edit,
        _ => null,
    };

    /// <summary>Whether <paramref name="fileName"/> (no folder) matches one of the masks.</summary>
    public bool Matches(string fileName) => _mask.Value?.Matches(fileName ?? "") == true;

    /// <summary>The same rule without the actions that name no program; null stays null.</summary>
    private static AssociationAction? Clean(AssociationAction? action) =>
        action is null || action.Program.Trim().Length == 0 ? null : action;
}
