using D3skCommander.Core.Settings;

namespace D3skCommander.App.Features.Settings;

/// <summary>A copy of the preferences, so the window edits a draft and Apply keeps every field it does not show.</summary>
public static class PreferencesCopy
{
    public static Preferences Of(Preferences source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Preferences
        {
            Language = source.Language,
            DarkMode = source.DarkMode,
            FontSize = source.FontSize,
            ShowHidden = source.ShowHidden,
            ShowDriveBar = source.ShowDriveBar,
            ShowToolbar = source.ShowToolbar,
            ShowTreeLeft = source.ShowTreeLeft,
            ShowTreeRight = source.ShowTreeRight,
            TreeWidthLeft = source.TreeWidthLeft,
            TreeWidthRight = source.TreeWidthRight,
            FavoriteTabsMode = source.FavoriteTabsMode,
            Toolbar = [.. source.Toolbar],
            Associations = [.. source.Associations],
            UseTrash = source.UseTrash,
            ConfirmDelete = source.ConfirmDelete,
            ConfirmTrashFallback = source.ConfirmTrashFallback,
            LogOperations = source.LogOperations,
            AutoFolderSizes = source.AutoFolderSizes,
            IndexRoots = source.IndexRoots,
            IndexExcludes = source.IndexExcludes,
            IndexRefreshHours = source.IndexRefreshHours,
            Editor = source.Editor,
            Terminal = source.Terminal,
            Columns = source.Columns,
            ColumnSets = [.. source.ColumnSets.Select(set => set.Clone())],
            CheckUpdates = source.CheckUpdates,
            UserCommands = [.. source.UserCommands],
        };
    }
}
