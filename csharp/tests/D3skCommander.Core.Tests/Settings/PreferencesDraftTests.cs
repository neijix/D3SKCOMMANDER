using D3skCommander.Core.Associations;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Toolbar;
using Xunit;

namespace D3skCommander.Core.Tests.Settings;

/// <summary>AC5: the draft comparison sees every field the window edits and nothing else.</summary>
public class PreferencesDraftTests
{
    private static readonly AssociationAction Edit = new("edit", "%s", "w");
    private static readonly AssociationAction Extra = new("viewer", "", "", "View");

    private static Preferences Prefs() => new()
    {
        Associations = [new AssociationRule("Text", "*.txt", Edit, null, null, [Extra])],
        ColumnSets = [new ColumnSet("Mine", [new ColumnSetColumn(ColumnId.Size, 80)])],
        UserCommands = [new UserCommand("Zip", "zip %s", false)],
        Toolbar = [ToolbarItem.ForProgram("tool", "-a", "w", "", "tip"), ToolbarItem.Separator],
    };

    private static FavoriteTabs Favs()
    {
        var set = new FavoriteTabSet { Name = "Work" };
        set.Left.Tabs.Add(new SessionTab { Directory = "/a" });
        set.Right.Tabs.Add(new SessionTab { Directory = "/b" });
        return new FavoriteTabs { Sets = [set] };
    }

    private static Preferences With(Action<Preferences> change)
    {
        var prefs = Prefs();
        change(prefs);
        return prefs;
    }

    private static FavoriteTabs FavsWith(Action<FavoriteTabSet> change)
    {
        var favs = Favs();
        change(favs.Sets[0]);
        return favs;
    }

    private static bool PrefsDiffer(Preferences candidate) =>
        PreferencesDraft.Differs(Prefs(), Favs(), candidate, Favs());

    private static bool FavsDiffer(FavoriteTabs candidate) =>
        PreferencesDraft.Differs(Prefs(), Favs(), Prefs(), candidate);

    private static bool RuleDiffers(AssociationRule rule) => PrefsDiffer(With(p => p.Associations = [rule]));

    [Fact]
    public void AnIndependentEqualCopyDoesNotDiffer() => Assert.False(PrefsDiffer(Prefs()));

    public static TheoryData<string> ScalarFields() => new()
    {
        "language", "font", "hidden", "trash", "confirm", "fallback", "log", "sizes", "updates",
        "roots", "excludes", "hours", "preview", "editor", "terminal", "mode",
    };

    [Theory]
    [MemberData(nameof(ScalarFields))]
    public void EachScalarFieldAloneDiffers(string field)
    {
        var prefs = Prefs();
        switch (field)
        {
            case "language": prefs.Language = prefs.Language == Language.Fr ? Language.En : Language.Fr; break;
            case "font": prefs.FontSize += 1; break;
            case "hidden": prefs.ShowHidden = !prefs.ShowHidden; break;
            case "trash": prefs.UseTrash = !prefs.UseTrash; break;
            case "confirm": prefs.ConfirmDelete = !prefs.ConfirmDelete; break;
            case "fallback": prefs.ConfirmTrashFallback = !prefs.ConfirmTrashFallback; break;
            case "log": prefs.LogOperations = !prefs.LogOperations; break;
            case "sizes": prefs.AutoFolderSizes = !prefs.AutoFolderSizes; break;
            case "updates": prefs.CheckUpdates = !prefs.CheckUpdates; break;
            case "roots": prefs.IndexRoots = "/x"; break;
            case "excludes": prefs.IndexExcludes += ";x"; break;
            case "hours": prefs.IndexRefreshHours += 1; break;
            case "preview": prefs.PreviewStartSeconds += 1; break;
            case "editor": prefs.Editor = "ed"; break;
            case "terminal": prefs.Terminal = "term"; break;
            case "mode": prefs.FavoriteTabsMode = FavoriteRestoreMode.Add; break;
            default: throw new ArgumentException(field);
        }

        Assert.True(PrefsDiffer(prefs));
    }

    [Fact]
    public void FieldsTheWindowDoesNotEditAreIgnored()
    {
        var prefs = Prefs();
        prefs.DarkMode = !prefs.DarkMode;
        prefs.ShowDriveBar = !prefs.ShowDriveBar;
        prefs.ShowToolbar = !prefs.ShowToolbar;
        prefs.ShowTreeLeft = !prefs.ShowTreeLeft;
        prefs.TreeWidthRight = 500;
        Assert.False(PrefsDiffer(prefs));
    }

    [Fact]
    public void AssociationEditsDiffer()
    {
        Assert.False(RuleDiffers(new AssociationRule("Text", "*.txt", Edit, null, null, [Extra])));
        Assert.True(PrefsDiffer(With(p => p.Associations = [])));
        Assert.True(RuleDiffers(new AssociationRule("T2", "*.txt", Edit, null, null, [Extra])));
        Assert.True(RuleDiffers(new AssociationRule("Text", "*.md", Edit, null, null, [Extra])));
        Assert.True(RuleDiffers(new AssociationRule("Text", "*.txt", Edit with { Arguments = "x" }, null, null, [Extra])));
        Assert.True(RuleDiffers(new AssociationRule("Text", "*.txt", Edit with { Program = "other" }, null, null, [Extra])));
        Assert.True(RuleDiffers(new AssociationRule("Text", "*.txt", Edit with { WorkingFolder = "z" }, null, null, [Extra])));
        Assert.True(RuleDiffers(new AssociationRule("Text", "*.txt", Edit, new AssociationAction("v"), null, [Extra])));
        Assert.True(RuleDiffers(new AssociationRule("Text", "*.txt", Edit, null, new AssociationAction("e"), [Extra])));
        Assert.True(RuleDiffers(new AssociationRule("Text", "*.txt", Edit, null, null, [Extra with { Name = "Other" }])));
        Assert.True(RuleDiffers(new AssociationRule("Text", "*.txt", Edit, null, null, [])));
    }

    [Fact]
    public void ColumnSetEditsDiffer()
    {
        Assert.False(PrefsDiffer(With(p => p.ColumnSets = [new ColumnSet("Mine", [new ColumnSetColumn(ColumnId.Size, 80)])])));
        Assert.True(PrefsDiffer(With(p => p.ColumnSets = [new ColumnSet("Mine", [new ColumnSetColumn(ColumnId.Size, 90)])])));
        Assert.True(PrefsDiffer(With(p => p.ColumnSets = [new ColumnSet("Mine", [new ColumnSetColumn(ColumnId.Modified, 80)])])));
        Assert.True(PrefsDiffer(With(p => p.ColumnSets = [new ColumnSet("Mine2", [new ColumnSetColumn(ColumnId.Size, 80)])])));
        Assert.True(PrefsDiffer(With(p => p.ColumnSets = [])));
    }

    [Fact]
    public void AnEmptyColumnSetListEqualsOneHoldingTheBuiltInDefaults()
    {
        var stored = new Preferences();
        var candidate = new Preferences { ColumnSets = [.. stored.AllColumnSets().Select(set => set.Clone())] };
        Assert.False(PreferencesDraft.Differs(stored, new FavoriteTabs(), candidate, new FavoriteTabs()));
    }

    [Fact]
    public void UserCommandEditsDiffer()
    {
        Assert.True(PrefsDiffer(With(p => p.UserCommands = [new UserCommand("Zip", "zip %s", true)])));
        Assert.True(PrefsDiffer(With(p => p.UserCommands = [new UserCommand("Zip", "zip", false)])));
        Assert.True(PrefsDiffer(With(p => p.UserCommands = [new UserCommand("Zap", "zip %s", false)])));
        Assert.True(PrefsDiffer(With(p => p.UserCommands = [])));
    }

    [Fact]
    public void ToolbarEditsDiffer()
    {
        var sep = ToolbarItem.Separator;
        Assert.False(PrefsDiffer(With(p => p.Toolbar = [ToolbarItem.ForProgram("tool", "-a", "w", "", "tip"), sep])));
        Assert.True(PrefsDiffer(With(p => p.Toolbar = [ToolbarItem.ForProgram("tool", "-a", "w", "", "tip2"), sep])));
        Assert.True(PrefsDiffer(With(p => p.Toolbar = [ToolbarItem.ForProgram("tool", "-b", "w", "", "tip"), sep])));
        Assert.True(PrefsDiffer(With(p => p.Toolbar = [ToolbarItem.ForProgram("tool", "-a", "x", "", "tip"), sep])));
        Assert.True(PrefsDiffer(With(p => p.Toolbar = [ToolbarItem.ForUserCommand("Zip"), sep])));
        Assert.True(PrefsDiffer(With(p => p.Toolbar = [sep, ToolbarItem.ForProgram("tool", "-a", "w", "", "tip")])));
        Assert.True(PrefsDiffer(With(p => p.Toolbar = [ToolbarItem.ForProgram("tool", "-a", "w", "", "tip")])));
        Assert.True(PrefsDiffer(With(p => p.Toolbar[0] = p.Toolbar[0] with { Kind = ToolbarItemKind.Command })));
        Assert.True(PrefsDiffer(With(p => p.Toolbar[0] = p.Toolbar[0] with { Command = CommandId.Copy })));
        Assert.True(PrefsDiffer(With(p => p.Toolbar[0] = p.Toolbar[0] with { UserCommandName = "Zip" })));
        Assert.True(PrefsDiffer(With(p => p.Toolbar[0] = p.Toolbar[0] with { Program = "other" })));
        Assert.True(PrefsDiffer(With(p => p.Toolbar[0] = p.Toolbar[0] with { Icon = ToolbarIcons.ForCommand(CommandId.Copy) })));
    }

    [Fact]
    public void FavoriteTabSetEditsDiffer()
    {
        Assert.False(FavsDiffer(Favs()));
        Assert.True(FavsDiffer(new FavoriteTabs()));
        Assert.True(FavsDiffer(FavsWith(set => set.Name = "Play")));
        Assert.True(FavsDiffer(FavsWith(set => set.ActiveSide = FavoriteSide.Right)));
        Assert.True(FavsDiffer(FavsWith(set => set.Left.ActiveIndex = 1)));
        Assert.True(FavsDiffer(FavsWith(set => set.Left.Tabs[0].Directory = "/z")));
        Assert.True(FavsDiffer(FavsWith(set => set.Left.Tabs[0].Sort = new SortOrder(SortKey.Size, true))));
        Assert.True(FavsDiffer(FavsWith(set => set.Right.Tabs[0].ViewMode = PanelViewMode.Brief)));
        Assert.True(FavsDiffer(FavsWith(set => set.Right.Tabs[0].ColumnSet = "Details")));
        Assert.True(FavsDiffer(FavsWith(set => set.Right.Tabs.Add(new SessionTab()))));
        Assert.True(FavsDiffer(FavsWith(set => set.Left.Tabs[0].ColumnWidths.Name += 1)));
        Assert.True(FavsDiffer(FavsWith(set => set.Left.Tabs[0].ColumnWidths.Extension += 1)));
        Assert.True(FavsDiffer(FavsWith(set => set.Left.Tabs[0].ColumnWidths.Size += 1)));
        Assert.True(FavsDiffer(FavsWith(set => set.Left.Tabs[0].ColumnWidths.Date += 1)));
        Assert.True(FavsDiffer(FavsWith(set => set.Left.Tabs[0].ColumnWidths.Attributes += 1)));
    }
}
