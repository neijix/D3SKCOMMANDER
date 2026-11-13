using D3skCommander.Core.Associations;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Panels;

namespace D3skCommander.Core.Settings;

/// <summary>
/// Whether a settings draft differs from what is stored, on the fields the settings window edits only.
/// The candidate is what Apply would save (already normalized); lists compare by value and in order.
/// </summary>
public static class PreferencesDraft
{
    /// <summary>True when <paramref name="candidate"/> or <paramref name="candidateFavorites"/> differs from the stored pair on a field the window edits.</summary>
    public static bool Differs(
        Preferences stored,
        FavoriteTabs storedFavorites,
        Preferences candidate,
        FavoriteTabs candidateFavorites)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(storedFavorites);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidateFavorites);
        return !SameScalars(stored, candidate)
            || !Same(stored.UserCommands, candidate.UserCommands, SameUserCommand)
            || !Same(stored.Toolbar, candidate.Toolbar, static (a, b) => a == b)
            || !Same(stored.Associations, candidate.Associations, SameRule)
            || !Same(stored.AllColumnSets(), candidate.AllColumnSets(), SameColumnSet)
            || !Same(storedFavorites.Sets, candidateFavorites.Sets, SameFavoriteSet);
    }

    private static bool SameScalars(Preferences a, Preferences b) =>
        a.Language == b.Language
        && a.Theme == b.Theme
        && a.FontSize.Equals(b.FontSize)
        && a.ShowHidden == b.ShowHidden
        && a.UseTrash == b.UseTrash
        && a.ConfirmDelete == b.ConfirmDelete
        && a.ConfirmTrashFallback == b.ConfirmTrashFallback
        && a.LogOperations == b.LogOperations
        && a.AutoFolderSizes == b.AutoFolderSizes
        && a.CheckUpdates == b.CheckUpdates
        && a.IndexRoots == b.IndexRoots
        && a.IndexExcludes == b.IndexExcludes
        && a.IndexRefreshHours == b.IndexRefreshHours
        && a.PreviewStartSeconds == b.PreviewStartSeconds
        && a.Editor == b.Editor
        && a.Terminal == b.Terminal
        && a.FavoriteTabsMode == b.FavoriteTabsMode;

    private static bool Same<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, T, bool> same) =>
        a.Count == b.Count && a.Zip(b).All(pair => same(pair.First, pair.Second));

    private static bool SameUserCommand(UserCommand a, UserCommand b) =>
        a.Name == b.Name && a.Command == b.Command && a.OncePerFile == b.OncePerFile;

    private static bool SameAction(AssociationAction? a, AssociationAction? b) =>
        a is null || b is null
            ? a is null && b is null
            : a.Program == b.Program && a.Arguments == b.Arguments && a.WorkingFolder == b.WorkingFolder && a.Name == b.Name;

    private static bool SameRule(AssociationRule a, AssociationRule b) =>
        a.Name == b.Name
        && a.Masks == b.Masks
        && SameAction(a.Open, b.Open)
        && SameAction(a.View, b.View)
        && SameAction(a.Edit, b.Edit)
        && Same(a.Extras, b.Extras, SameAction);

    private static bool SameColumnSet(ColumnSet a, ColumnSet b) =>
        a.Name == b.Name
        && Same(a.Columns, b.Columns, static (x, y) => x.Id == y.Id && x.Width.Equals(y.Width));

    private static bool SameFavoriteSet(FavoriteTabSet a, FavoriteTabSet b) =>
        a.Name == b.Name
        && a.ActiveSide == b.ActiveSide
        && SamePanel(a.Left, b.Left)
        && SamePanel(a.Right, b.Right);

    private static bool SamePanel(SessionPanel a, SessionPanel b) =>
        a.ActiveIndex == b.ActiveIndex && Same(a.Tabs, b.Tabs, SameTab);

    private static bool SameTab(SessionTab a, SessionTab b) =>
        a.Directory == b.Directory
        && a.Sort == b.Sort
        && a.ViewMode == b.ViewMode
        && a.ColumnSet == b.ColumnSet
        && a.ColumnWidths.Name.Equals(b.ColumnWidths.Name)
        && a.ColumnWidths.Extension.Equals(b.ColumnWidths.Extension)
        && a.ColumnWidths.Size.Equals(b.ColumnWidths.Size)
        && a.ColumnWidths.Date.Equals(b.ColumnWidths.Date)
        && a.ColumnWidths.Attributes.Equals(b.ColumnWidths.Attributes);
}
