namespace D3skCommander.Core.Commands;

/// <summary>i18n key of each <see cref="CommandId"/>, in enum order. Keys live in the commands catalog.</summary>
internal static class CommandLabelKeys
{
    public static IReadOnlyList<string> All { get; } = Merge(Early(), Late());

    private static string[] Early() =>
    [
        "cmd.view",
        "cmd.edit",
        "cmd.edit_new",
        "cmd.copy",
        "cmd.move",
        "cmd.rename",
        "cmd.mkdir",
        "cmd.delete",
        "cmd.delete_perm",
        "cmd.pack",
        "cmd.unpack",
        "cmd.backup",
        "cmd.properties",
        "cmd.open_with",
        "cmd.quit",
        "cmd.select_group",
        "cmd.unselect_group",
        "cmd.select_all",
        "cmd.unselect_all",
        "cmd.invert",
        "cmd.select_same_ext",
        "cmd.copy_names",
        "cmd.copy_paths",
        "cmd.compare_dirs",
        "cmd.search",
        "cmd.multirename",
        "cmd.compare_files",
        "cmd.sync",
        "cmd.split",
        "cmd.combine",
        "cmd.checksum_create",
        "cmd.checksum_verify",
        "cmd.addtime",
        "cmd.redate",
        "cmd.touch",
        "cmd.catalog",
        "cmd.encrypt",
        "cmd.decrypt",
        "cmd.dir_size",
        "cmd.ftp",
    ];

    private static string[] Late() =>
    [
        "cmd.passwords",
        "cmd.net_connect",
        "cmd.net_disconnect",
        "cmd.browse_folder",
        "cmd.quick_view",
        "cmd.full",
        "cmd.brief",
        "cmd.thumbnails",
        "cmd.hidden",
        "cmd.branch",
        "cmd.quick_filter",
        "cmd.refresh",
        "cmd.swap",
        "cmd.same_dir",
        "cmd.new_tab",
        "cmd.close_tab",
        "cmd.next_tab",
        "cmd.hotlist",
        "cmd.add_hotlist",
        "cmd.history",
        "cmd.back",
        "cmd.forward",
        "cmd.parent",
        "cmd.root",
        "cmd.home",
        "cmd.terminal",
        "cmd.settings",
        "cmd.settings_folder",
        "cmd.log",
        "cmd.about",
        "cmd.check_update",
        "cmd.keys",
        "cmd.sort_name",
        "cmd.sort_ext",
        "cmd.sort_date",
        "cmd.sort_size",
        "cmd.switch_panel",
        "cmd.drives_left",
        "cmd.drives_right",
    ];

    private static string[] Merge(string[] early, string[] late)
    {
        var all = new string[early.Length + late.Length];
        early.CopyTo(all, 0);
        late.CopyTo(all, early.Length);
        return all;
    }
}
