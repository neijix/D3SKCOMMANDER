using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.Core.Tests.Commands;

public class CommandRegistryTests
{
    [Fact]
    public void CommandsResolveByName()
    {
        Assert.Equal(CommandId.Sync, CommandCatalog.FromName("sync"));
        Assert.Equal(CommandId.Sync, CommandCatalog.FromName("  SYNC  "));
        Assert.Null(CommandCatalog.FromName("Nope"));
        Assert.Equal(86, CommandCatalog.All.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in CommandCatalog.All)
        {
            Assert.True(seen.Add(command.ToString()));
            Assert.Equal(command, CommandCatalog.FromName(command.ToString().ToLowerInvariant()));
        }
    }

    [Fact]
    public void EachPanelBarOpensItsOwnDrivesThenTheSharedPlaces()
    {
        CommandId[] shared = [CommandId.Parent, CommandId.Hotlist, CommandId.History, CommandId.NetConnect, CommandId.Ftp];
        Assert.Equal([CommandId.DrivesLeft, .. shared], CommandCatalog.PanelBar(leftPanel: true));
        Assert.Equal([CommandId.DrivesRight, .. shared], CommandCatalog.PanelBar(leftPanel: false));
    }

    [Fact]
    public void EveryCommandHasItsOwnTranslatedLabel()
    {
        var localizer = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var seen = new Dictionary<string, CommandId>(StringComparer.Ordinal);
        foreach (var command in CommandCatalog.All)
        {
            var key = CommandCatalog.LabelKey(command);
            Assert.NotEqual(key, localizer.Get(key, Language.En));
            Assert.NotEqual(key, localizer.Get(key, Language.Fr));
            Assert.False(seen.ContainsKey(key), $"{command} shares {key}");
            seen[key] = command;
        }

        Assert.Equal(86, seen.Count);
        Assert.Equal("View file", localizer.Get(CommandCatalog.LabelKey(CommandId.View), Language.En));
        Assert.Equal("Quitter", localizer.Get(CommandCatalog.LabelKey(CommandId.Quit), Language.Fr));
        AssertEarlyKeys();
        AssertLateKeys();
    }

    [Fact]
    public void NoShortcutBoundTwice()
    {
        var seen = new HashSet<Gesture>();
        foreach (var binding in CommandCatalog.Bindings)
        {
            Assert.True(seen.Add(binding.Gesture), $"duplicate shortcut {binding.Gesture}");
        }

        Assert.Equal(60, seen.Count);
    }

    [Fact]
    public void ModifiedGesturesTakePrecedence()
    {
        var bindings = CommandCatalog.Bindings;
        var firstPlain = -1;
        for (var i = 0; i < bindings.Count; i++)
        {
            if (bindings[i].Gesture.Modifiers == GestureModifiers.None)
            {
                firstPlain = i;
                break;
            }
        }

        Assert.True(firstPlain > 0);
        for (var i = firstPlain; i < bindings.Count; i++)
        {
            Assert.Equal(GestureModifiers.None, bindings[i].Gesture.Modifiers);
        }

        var ctrlShift = GestureModifiers.Ctrl | GestureModifiers.Shift;
        Assert.Equal(new Gesture(ctrlShift, GestureKey.F1), bindings[0].Gesture);
        Assert.Equal(CommandId.RenameInPlace, CommandCatalog.Match(new Gesture(GestureModifiers.Shift, GestureKey.F6)));
        Assert.Equal(CommandId.Move, CommandCatalog.Match(new Gesture(GestureModifiers.None, GestureKey.F6)));
        Assert.Equal(CommandId.DeletePermanent, CommandCatalog.Match(new Gesture(GestureModifiers.Shift, GestureKey.F8)));
        Assert.Equal(CommandId.Delete, CommandCatalog.Match(new Gesture(GestureModifiers.None, GestureKey.F8)));
        Assert.Equal(CommandId.Quit, CommandCatalog.Match(new Gesture(GestureModifiers.Alt, GestureKey.F4)));
        Assert.Equal(CommandId.Edit, CommandCatalog.Match(new Gesture(GestureModifiers.None, GestureKey.F4)));
        Assert.Equal(CommandId.DrivesLeft, CommandCatalog.Match(new Gesture(GestureModifiers.Alt, GestureKey.F1)));
        Assert.Equal(CommandId.Keys, CommandCatalog.Match(new Gesture(GestureModifiers.None, GestureKey.F1)));
    }

    [Fact]
    public void DoubledCommandsKeepBothGestures()
    {
        Assert.Equal(
            new[] { Chord(GestureModifiers.Shift, GestureKey.F6), Chord(GestureModifiers.None, GestureKey.F2) },
            CommandCatalog.Gestures(CommandId.RenameInPlace));
        Assert.Equal(
            new[] { Chord(GestureModifiers.None, GestureKey.F8), Chord(GestureModifiers.None, GestureKey.Delete) },
            CommandCatalog.Gestures(CommandId.Delete));
        Assert.Equal(
            new[] { Chord(GestureModifiers.Shift, GestureKey.F8), Chord(GestureModifiers.Shift, GestureKey.Delete) },
            CommandCatalog.Gestures(CommandId.DeletePermanent));
        Assert.Equal(
            new[] { Chord(GestureModifiers.Ctrl, GestureKey.PageUp), Chord(GestureModifiers.None, GestureKey.Backspace) },
            CommandCatalog.Gestures(CommandId.Parent));
        Assert.Equal(
            new[] { Chord(GestureModifiers.Ctrl, GestureKey.Plus), Chord(GestureModifiers.Ctrl, GestureKey.Equals) },
            CommandCatalog.Gestures(CommandId.SelectSameExt));
    }

    private static Gesture Chord(GestureModifiers modifiers, GestureKey key) => new(modifiers, key);

    private static void AssertEarlyKeys()
    {
        AssertKey(CommandId.View, "cmd.view");
        AssertKey(CommandId.Edit, "cmd.edit");
        AssertKey(CommandId.EditNew, "cmd.edit_new");
        AssertKey(CommandId.Copy, "cmd.copy");
        AssertKey(CommandId.Move, "cmd.move");
        AssertKey(CommandId.RenameInPlace, "cmd.rename");
        AssertKey(CommandId.MkDir, "cmd.mkdir");
        AssertKey(CommandId.Delete, "cmd.delete");
        AssertKey(CommandId.DeletePermanent, "cmd.delete_perm");
        AssertKey(CommandId.Pack, "cmd.pack");
        AssertKey(CommandId.Unpack, "cmd.unpack");
        AssertKey(CommandId.BackupZip, "cmd.backup");
        AssertKey(CommandId.Properties, "cmd.properties");
        AssertKey(CommandId.OpenWith, "cmd.open_with");
        AssertKey(CommandId.Quit, "cmd.quit");
        AssertKey(CommandId.SelectGroup, "cmd.select_group");
        AssertKey(CommandId.UnselectGroup, "cmd.unselect_group");
        AssertKey(CommandId.SelectAll, "cmd.select_all");
        AssertKey(CommandId.UnselectAll, "cmd.unselect_all");
        AssertKey(CommandId.Invert, "cmd.invert");
        AssertKey(CommandId.SelectSameExt, "cmd.select_same_ext");
        AssertKey(CommandId.CopyNames, "cmd.copy_names");
        AssertKey(CommandId.CopyPaths, "cmd.copy_paths");
        AssertKey(CommandId.ComparePanels, "cmd.compare_dirs");
        AssertKey(CommandId.Search, "cmd.search");
        AssertKey(CommandId.MultiRename, "cmd.multirename");
        AssertKey(CommandId.CompareFiles, "cmd.compare_files");
        AssertKey(CommandId.Sync, "cmd.sync");
        AssertKey(CommandId.Split, "cmd.split");
        AssertKey(CommandId.Combine, "cmd.combine");
        AssertKey(CommandId.ChecksumCreate, "cmd.checksum_create");
        AssertKey(CommandId.ChecksumVerify, "cmd.checksum_verify");
        AssertKey(CommandId.AddTime, "cmd.addtime");
        AssertKey(CommandId.ReDate, "cmd.redate");
        AssertKey(CommandId.Touch, "cmd.touch");
        AssertKey(CommandId.Catalog, "cmd.catalog");
        AssertKey(CommandId.Encrypt, "cmd.encrypt");
        AssertKey(CommandId.Decrypt, "cmd.decrypt");
        AssertKey(CommandId.DirSizes, "cmd.dir_size");
        AssertKey(CommandId.Ftp, "cmd.ftp");
    }

    private static void AssertLateKeys()
    {
        AssertKey(CommandId.Passwords, "cmd.passwords");
        AssertKey(CommandId.NetConnect, "cmd.net_connect");
        AssertKey(CommandId.NetDisconnect, "cmd.net_disconnect");
        AssertKey(CommandId.BrowseFolder, "cmd.browse_folder");
        AssertKey(CommandId.QuickView, "cmd.quick_view");
        AssertKey(CommandId.ViewFull, "cmd.full");
        AssertKey(CommandId.ViewBrief, "cmd.brief");
        AssertKey(CommandId.ViewThumbs, "cmd.thumbnails");
        AssertKey(CommandId.ToggleHidden, "cmd.hidden");
        AssertKey(CommandId.Branch, "cmd.branch");
        AssertKey(CommandId.QuickFilter, "cmd.quick_filter");
        AssertKey(CommandId.Refresh, "cmd.refresh");
        AssertKey(CommandId.Swap, "cmd.swap");
        AssertKey(CommandId.SameDir, "cmd.same_dir");
        AssertKey(CommandId.NewTab, "cmd.new_tab");
        AssertKey(CommandId.CloseTab, "cmd.close_tab");
        AssertKey(CommandId.NextTab, "cmd.next_tab");
        AssertKey(CommandId.Hotlist, "cmd.hotlist");
        AssertKey(CommandId.AddHotlist, "cmd.add_hotlist");
        AssertKey(CommandId.History, "cmd.history");
        AssertKey(CommandId.Back, "cmd.back");
        AssertKey(CommandId.Forward, "cmd.forward");
        AssertKey(CommandId.Parent, "cmd.parent");
        AssertKey(CommandId.Root, "cmd.root");
        AssertKey(CommandId.Home, "cmd.home");
        AssertKey(CommandId.GoToContainingFolder, "cmd.go_to_folder");
        AssertKey(CommandId.SelectAllButFirst, "cmd.select_all_but_first");
        AssertKey(CommandId.DiskUsage, "cmd.disk_usage");
        AssertKey(CommandId.Terminal, "cmd.terminal");
        AssertKey(CommandId.Settings, "cmd.settings");
        AssertKey(CommandId.SettingsFolder, "cmd.settings_folder");
        AssertKey(CommandId.Log, "cmd.log");
        AssertKey(CommandId.About, "cmd.about");
        AssertKey(CommandId.CheckUpdate, "cmd.check_update");
        AssertKey(CommandId.Keys, "cmd.keys");
        AssertKey(CommandId.SortName, "cmd.sort_name");
        AssertKey(CommandId.SortExt, "cmd.sort_ext");
        AssertKey(CommandId.SortDate, "cmd.sort_date");
        AssertKey(CommandId.SortSize, "cmd.sort_size");
        AssertKey(CommandId.SwitchPanel, "cmd.switch_panel");
        AssertKey(CommandId.DrivesLeft, "cmd.drives_left");
        AssertKey(CommandId.DrivesRight, "cmd.drives_right");
    }

    private static void AssertKey(CommandId command, string key) =>
        Assert.Equal(key, CommandCatalog.LabelKey(command));
}
