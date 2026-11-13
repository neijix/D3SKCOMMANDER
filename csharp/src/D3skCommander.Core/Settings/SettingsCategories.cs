using D3skCommander.Core.Commands;

namespace D3skCommander.Core.Settings;

/// <summary>One page of the settings window: a stable id and the localizer key of its name.</summary>
public sealed record SettingsCategory(string Id, string NameKey);

/// <summary>The settings window's categories in display order, and the commands that deep-link to one.</summary>
public static class SettingsCategories
{
    public const string General = "general";
    public const string Panels = "panels";
    public const string FileOperations = "file-operations";
    public const string Programs = "programs";
    public const string Columns = "columns";
    public const string Favorites = "favorites";
    public const string FavoriteTabs = "favorite-tabs";
    public const string FileAssociations = "file-associations";
    public const string Toolbar = "toolbar";
    public const string UserCommands = "user-commands";
    public const string QuickFindIndex = "quick-find-index";
    public const string SettingsFolder = "settings-folder";

    public static IReadOnlyList<SettingsCategory> All { get; } =
    [
        new(General, "settings.cat_general"),
        new(Panels, "settings.cat_panels"),
        new(FileOperations, "settings.cat_file_ops"),
        new(Programs, "settings.cat_programs"),
        new(Columns, "settings.cat_columns"),
        new(Favorites, "hotlist.title"),
        new(FavoriteTabs, "favorites.title"),
        new(FileAssociations, "assoc.title"),
        new(Toolbar, "toolbar.title"),
        new(UserCommands, "settings.cat_user_commands"),
        new(QuickFindIndex, "quickfind.settings.title"),
        new(SettingsFolder, "settings.folder"),
    ];

    /// <summary>The category a command opens the window on, or null when it has none.</summary>
    public static SettingsCategory? ForCommand(CommandId command)
    {
        var id = command switch
        {
            CommandId.EditColumnSets => Columns,
            CommandId.CustomizeToolbar => Toolbar,
            CommandId.ManageFavorites => Favorites,
            CommandId.ManageFavoriteTabs => FavoriteTabs,
            CommandId.EditFileAssociations => FileAssociations,
            _ => null,
        };
        return id is null ? null : All.First(c => c.Id == id);
    }
}
