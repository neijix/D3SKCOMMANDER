using System.Globalization;
using System.Text.Json;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;
using D3skCommander.Core.Toolbar;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace D3skCommander.Core.Tests.Settings;

/// <summary>A10: each store round-trips with its schema, saves atomically, and debounces. P6 load defaults.</summary>
public class SettingsStoreTests
{
    [Fact]
    public void PreferencesRoundTripKeepsSchemaAndFields()
    {
        using var dir = new Scratch();
        var clock = Clock();
        var preferences = new Preferences
        {
            Language = Language.Fr,
            DarkMode = false,
            Theme = ThemeMode.Dark,
            FontSize = 15.5f,
            ShowHidden = true,
            UseTrash = false,
            ConfirmDelete = false,
            ConfirmTrashFallback = false,
            LogOperations = true,
            AutoFolderSizes = true,
            IndexRoots = "/data;/home",
            IndexExcludes = "*/tmp",
            IndexRefreshHours = 6,
            ShowDriveBar = false,
            ShowToolbar = false,
            FavoriteTabsMode = FavoriteRestoreMode.Add,
            ShowTreeLeft = true,
            ShowTreeRight = true,
            TreeWidthLeft = 300,
            TreeWidthRight = 150,
            Toolbar = [ToolbarItem.ForCommand(D3skCommander.Core.Commands.CommandId.Copy, "copy", "Copy it"), ToolbarItem.Separator, ToolbarItem.ForProgram("/opt/my tool/run", "--x %s", "/tmp")],
            Editor = "vi",
            Terminal = "xterm",
            Columns = new Columns(false, true, false, true),
            ColumnSets = [new ColumnSet("Photos", [new ColumnSetColumn(ColumnId.Size, 90), new ColumnSetColumn(ColumnId.Created)])],
            CheckUpdates = false,
            UserCommands = [new UserCommand("Edit", "vi %s", true), new UserCommand("List", "ls", false)],
        };

        using (var store = new PreferencesStore(dir.Folder, clock))
        {
            store.Save(preferences);
            Settle(clock);
        }

        Assert.Equal(1, Preferences.SchemaVersion);
        Assert.Equal(Preferences.SchemaVersion, Schema(Prefs(dir)));
        using var loaded = new PreferencesStore(dir.Folder, clock);
        AssertSame(preferences, loaded.Current);
        Assert.Null(loaded.TakeLoadError());
        Assert.DoesNotContain(SettingsFileNames.Sites, Names(dir.Folder));
    }

    [Fact]
    public void SessionRoundTripKeepsSchemaAndFields()
    {
        using var dir = new Scratch();
        var clock = Clock();
        var session = SampleSession();

        using (var store = new SessionStore(dir.Folder, clock))
        {
            store.Save(session);
            Settle(clock);
        }

        Assert.Equal(1, Session.SchemaVersion);
        Assert.Equal(Session.SchemaVersion, Schema(Path.Combine(dir.Folder, SettingsFileNames.Session)));
        using var loaded = new SessionStore(dir.Folder, clock);
        AssertSame(session, loaded.Current);
    }

    [Fact]
    public void HotlistRoundTripKeepsSchemaAndFields()
    {
        using var dir = new Scratch();
        var clock = Clock();
        var hotlist = new Hotlist
        {
            Items = [new HotlistItem("Docs", "/home/docs"), new HotlistItem("Work", "/var/work")],
        };

        using (var store = new HotlistStore(dir.Folder, clock))
        {
            store.Save(hotlist);
            Settle(clock);
        }

        Assert.Equal(1, Hotlist.SchemaVersion);
        Assert.Equal(Hotlist.SchemaVersion, Schema(Path.Combine(dir.Folder, SettingsFileNames.Hotlist)));
        using var loaded = new HotlistStore(dir.Folder, clock);
        Assert.Equal(2, loaded.Current.Items.Count);
        Assert.Equal("Docs", loaded.Current.Items[0].Name);
        Assert.Equal("/home/docs", loaded.Current.Items[0].Path);
        Assert.Equal("Work", loaded.Current.Items[1].Name);
        Assert.Equal("/var/work", loaded.Current.Items[1].Path);
    }

    [Fact]
    public void SaveReplacesTheFileAtomically()
    {
        using var dir = new Scratch();
        var clock = Clock();
        var path = Prefs(dir);
        File.WriteAllText(path, "{\"schema\":1,\"fontSize\":11,\"showHidden\":true}");

        using (var store = new PreferencesStore(dir.Folder, clock))
        {
            Assert.Equal(11f, store.Current.FontSize);
            store.Save(new Preferences { FontSize = 19f, ShowHidden = false });
            Settle(clock);
        }

        using var loaded = new PreferencesStore(dir.Folder, clock);
        Assert.Equal(19f, loaded.Current.FontSize);
        Assert.False(loaded.Current.ShowHidden);
        Assert.Equal([SettingsFileNames.Preferences], Names(dir.Folder));
    }

    [Fact]
    public void SavesInsideTheWindowWriteOnce()
    {
        using var dir = new Scratch();
        var clock = Clock();
        var path = Prefs(dir);
        Assert.Equal(150, SettingsStore<Preferences>.DebounceMilliseconds);

        using var store = new PreferencesStore(dir.Folder, clock);
        store.Save(new Preferences { FontSize = 18f });
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.False(File.Exists(path));

        store.Save(new Preferences { FontSize = 20f });
        clock.Advance(TimeSpan.FromMilliseconds(SettingsStore<Preferences>.DebounceMilliseconds - 1));
        Assert.False(File.Exists(path));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        using var loaded = new PreferencesStore(dir.Folder, clock);
        Assert.Equal(20f, loaded.Current.FontSize);
        Assert.Equal([SettingsFileNames.Preferences], Names(dir.Folder));
    }

    [Fact]
    public void MissingFileLoadsDefaults()
    {
        using var dir = new Scratch();
        var clock = Clock();
        using var preferences = new PreferencesStore(dir.Folder, clock);
        AssertDefaults(preferences.Current);
        Assert.Null(preferences.TakeLoadError());
        Assert.Empty(Directory.GetFiles(dir.Folder));

        var absent = Path.Combine(dir.Folder, "nope");
        using var missingFolder = new PreferencesStore(absent, clock);
        Assert.Equal(Preferences.DefaultFontSize, missingFolder.Current.FontSize);
        Assert.False(Directory.Exists(absent));

        using var session = new SessionStore(dir.Folder, clock);
        Assert.Empty(session.Current.Left.Tabs);
        Assert.Empty(session.Current.Right.Tabs);
        Assert.Empty(session.Current.History);
        Assert.Empty(session.Current.NetShares);
        Assert.Empty(session.Current.ToolWindows);
        Assert.Equal(PanelViewMode.Full, new SessionTab().ViewMode);
        Assert.Equal(SortOrder.NameAscending, new SessionTab().Sort);
        AssertWidths(new ColumnWidths());

        using var hotlist = new HotlistStore(dir.Folder, clock);
        Assert.Empty(hotlist.Current.Items);
    }

    [Fact]
    public void RoundTripAndDefaultsForMissingFields()
    {
        using var dir = new Scratch();
        var clock = Clock();
        var session = new Session();
        session.RememberDirectory("/a");
        session.RememberDirectory("/b");
        session.RememberDirectory("/a");
        Assert.Equal(["/a", "/b"], session.History);

        using (var preferences = new PreferencesStore(dir.Folder, clock))
        using (var sessions = new SessionStore(dir.Folder, clock))
        {
            preferences.Save(new Preferences { ShowHidden = true });
            sessions.Save(session);
            Settle(clock);
        }

        using (var preferences = new PreferencesStore(dir.Folder, clock))
        using (var sessions = new SessionStore(dir.Folder, clock))
        {
            Assert.True(preferences.Current.ShowHidden);
            Assert.Equal(["/a", "/b"], sessions.Current.History);
        }

        File.WriteAllText(Prefs(dir), "{\"showHidden\":true}");
        using var partial = new PreferencesStore(dir.Folder, clock);
        Assert.True(partial.Current.ShowHidden);
        Assert.True(partial.Current.UseTrash);

        using var missing = new PreferencesStore(Path.Combine(dir.Folder, "missing"), clock);
        Assert.Equal(14f, missing.Current.FontSize);
    }

    [Fact]
    public void PartialFileKeepsDefaultsForMissingFields()
    {
        using var dir = new Scratch();
        var clock = Clock();
        File.WriteAllText(Prefs(dir), "{\"showHidden\":true,\"ftpSites\":[],\"fontSize\":18}");
        using (var preferences = new PreferencesStore(dir.Folder, clock))
        {
            Assert.True(preferences.Current.ShowHidden);
            Assert.Equal(18f, preferences.Current.FontSize);
            Assert.True(preferences.Current.UseTrash);
            Assert.True(preferences.Current.DarkMode);
            Assert.True(preferences.Current.CheckUpdates);
            Assert.Equal(Columns.Default, preferences.Current.Columns);
            Assert.Null(preferences.TakeLoadError());
        }

        File.WriteAllText(Prefs(dir), "{\"schema\":1,\"columns\":{\"extension\":false}}");
        using var columns = new PreferencesStore(dir.Folder, clock);
        Assert.False(columns.Current.Columns.Extension);
        Assert.True(columns.Current.Columns.Size);
        Assert.True(columns.Current.Columns.Date);
        Assert.True(columns.Current.Columns.Attributes);
        Assert.Equal(Preferences.DefaultFontSize, columns.Current.FontSize);
    }

    [Fact]
    public void TruncatedFileLoadsDefaults()
    {
        using var dir = new Scratch();
        var clock = Clock();
        var path = Prefs(dir);
        File.WriteAllText(path, "{\"showHidden\":true");

        using var store = new PreferencesStore(dir.Folder, clock);
        AssertDefaults(store.Current);
        Assert.Null(store.TakeLoadError());
        Assert.Equal([SettingsFileNames.Preferences], Names(dir.Folder));
        Assert.Equal("{\"showHidden\":true", File.ReadAllText(path));
    }

    [Fact]
    public void EmptyFileLoadsDefaults()
    {
        using var dir = new Scratch();
        var clock = Clock();
        File.WriteAllText(Prefs(dir), "  \n");

        using var store = new PreferencesStore(dir.Folder, clock);
        Assert.Equal(Preferences.DefaultFontSize, store.Current.FontSize);
        Assert.Null(store.TakeLoadError());
        Assert.Equal([SettingsFileNames.Preferences], Names(dir.Folder));
    }

    [Fact]
    public void UnreadableFileIsRenamedBeforeTheFirstSave()
    {
        using var dir = new Scratch();
        var clock = Clock();
        var path = Prefs(dir);
        File.WriteAllText(path, "hello");

        using var store = new PreferencesStore(dir.Folder, clock);
        AssertDefaults(store.Current);
        var error = store.TakeLoadError();
        Assert.NotNull(error);
        Assert.Equal(ErrorKind.InvalidData, error.Kind);
        Assert.Equal("unreadable settings file", error.Message);
        Assert.Equal(path, error.Path);
        Assert.Null(store.TakeLoadError());

        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var renamed = path + ".corrupt-" + stamp;
        Assert.Equal("hello", File.ReadAllText(renamed));
        Assert.False(File.Exists(path));

        store.Save(store.Current);
        Settle(clock);
        Assert.Equal("hello", File.ReadAllText(renamed));
        using var restored = new PreferencesStore(dir.Folder, clock);
        AssertDefaults(restored.Current);
        Assert.Null(restored.TakeLoadError());
    }

    [Fact]
    public void WrongSchemaIsKeptAndNotApplied()
    {
        using var dir = new Scratch();
        var clock = Clock();
        var path = Prefs(dir);
        File.WriteAllText(path, "{\"schema\":2,\"fontSize\":99}");

        using var store = new PreferencesStore(dir.Folder, clock);
        Assert.Equal(Preferences.DefaultFontSize, store.Current.FontSize);
        Assert.NotNull(store.TakeLoadError());
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        Assert.Equal("{\"schema\":2,\"fontSize\":99}", File.ReadAllText(path + ".corrupt-" + stamp));
    }

    [Fact]
    public void HistoryKeepsTheLatestVisitFirst()
    {
        var session = new Session();
        session.RememberDirectory("/a");
        session.RememberDirectory("/b");
        session.RememberDirectory("/a");
        Assert.Equal(["/a", "/b"], session.History);

        for (var i = 0; i < Session.HistoryLimit; i++)
        {
            session.RememberDirectory("/n" + i.ToString(CultureInfo.InvariantCulture));
        }

        Assert.Equal(Session.HistoryLimit, session.History.Count);
        Assert.Equal("/n29", session.History[0]);
        Assert.DoesNotContain("/a", session.History);
        Assert.DoesNotContain("/b", session.History);
    }

    private static void AssertDefaults(Preferences preferences)
    {
        Assert.Equal(Languages.Detect(), preferences.Language);
        Assert.True(preferences.DarkMode);
        Assert.Equal(ThemeMode.System, preferences.Theme);
        Assert.Equal(Preferences.DefaultFontSize, preferences.FontSize);
        Assert.False(preferences.ShowHidden);
        Assert.True(preferences.UseTrash);
        Assert.True(preferences.ConfirmDelete);
        Assert.True(preferences.ConfirmTrashFallback);
        Assert.False(preferences.LogOperations);
        Assert.False(preferences.AutoFolderSizes);
        Assert.Equal("", preferences.IndexRoots);
        Assert.Equal(D3skCommander.Core.Index.IndexOptions.DefaultExcludes, preferences.IndexExcludes);
        Assert.Equal(24, preferences.IndexRefreshHours);
        Assert.True(preferences.ShowDriveBar);
        Assert.True(preferences.ShowToolbar);
        Assert.Equal(FavoriteRestoreMode.Replace, preferences.FavoriteTabsMode);
        Assert.False(preferences.ShowTreeLeft);
        Assert.False(preferences.ShowTreeRight);
        Assert.Equal(220, preferences.TreeWidthLeft);
        Assert.Equal(220, preferences.TreeWidthRight);
        Assert.Equal(ToolbarDefaults.Items(), preferences.Toolbar);
        Assert.Equal("", preferences.Editor);
        Assert.Equal("", preferences.Terminal);
        Assert.Equal(Columns.Default, preferences.Columns);
        Assert.Empty(preferences.ColumnSets);
        Assert.True(preferences.CheckUpdates);
        Assert.Empty(preferences.UserCommands);
    }

    private static void AssertSame(Preferences expected, Preferences actual)
    {
        Assert.Equal(expected.Language, actual.Language);
        Assert.Equal(expected.DarkMode, actual.DarkMode);
        Assert.Equal(expected.Theme, actual.Theme);
        Assert.Equal(expected.FontSize, actual.FontSize);
        Assert.Equal(expected.ShowHidden, actual.ShowHidden);
        Assert.Equal(expected.UseTrash, actual.UseTrash);
        Assert.Equal(expected.ConfirmDelete, actual.ConfirmDelete);
        Assert.Equal(expected.ConfirmTrashFallback, actual.ConfirmTrashFallback);
        Assert.Equal(expected.LogOperations, actual.LogOperations);
        Assert.Equal(expected.AutoFolderSizes, actual.AutoFolderSizes);
        Assert.Equal(expected.IndexRoots, actual.IndexRoots);
        Assert.Equal(expected.IndexExcludes, actual.IndexExcludes);
        Assert.Equal(expected.IndexRefreshHours, actual.IndexRefreshHours);
        Assert.Equal(expected.ShowDriveBar, actual.ShowDriveBar);
        Assert.Equal(expected.ShowToolbar, actual.ShowToolbar);
        Assert.Equal(expected.FavoriteTabsMode, actual.FavoriteTabsMode);
        Assert.Equal(expected.ShowTreeLeft, actual.ShowTreeLeft);
        Assert.Equal(expected.ShowTreeRight, actual.ShowTreeRight);
        Assert.Equal(expected.TreeWidthLeft, actual.TreeWidthLeft);
        Assert.Equal(expected.TreeWidthRight, actual.TreeWidthRight);
        Assert.Equal(expected.Toolbar, actual.Toolbar);
        Assert.Equal(expected.Editor, actual.Editor);
        Assert.Equal(expected.Terminal, actual.Terminal);
        Assert.Equal(expected.Columns, actual.Columns);
        Assert.Equal(expected.ColumnSets.Count, actual.ColumnSets.Count);
        for (var i = 0; i < expected.ColumnSets.Count; i++)
        {
            Assert.Equal(expected.ColumnSets[i].Name, actual.ColumnSets[i].Name);
            Assert.Equal(expected.ColumnSets[i].Signature(), actual.ColumnSets[i].Signature());
        }

        Assert.Equal(expected.CheckUpdates, actual.CheckUpdates);
        Assert.Equal(expected.UserCommands.Count, actual.UserCommands.Count);
        for (var i = 0; i < expected.UserCommands.Count; i++)
        {
            Assert.Equal(expected.UserCommands[i].Name, actual.UserCommands[i].Name);
            Assert.Equal(expected.UserCommands[i].Command, actual.UserCommands[i].Command);
            Assert.Equal(expected.UserCommands[i].OncePerFile, actual.UserCommands[i].OncePerFile);
        }
    }

    private static void AssertSame(Session expected, Session actual)
    {
        AssertPanel(expected.Left, actual.Left);
        AssertPanel(expected.Right, actual.Right);
        Assert.Equal(expected.History, actual.History);
        Assert.Equal(expected.NetShares, actual.NetShares);
        Assert.Equal(expected.ToolWindows.Count, actual.ToolWindows.Count);
        Assert.Equal(expected.ToolWindows[0].Id, actual.ToolWindows[0].Id);
        Assert.Equal(expected.ToolWindows[0].X, actual.ToolWindows[0].X);
        Assert.Equal(expected.ToolWindows[0].Y, actual.ToolWindows[0].Y);
        Assert.Equal(expected.ToolWindows[0].Width, actual.ToolWindows[0].Width);
        Assert.Equal(expected.ToolWindows[0].Height, actual.ToolWindows[0].Height);
    }

    private static void AssertPanel(SessionPanel expected, SessionPanel actual)
    {
        Assert.Equal(expected.ActiveIndex, actual.ActiveIndex);
        Assert.Equal(expected.Tabs.Count, actual.Tabs.Count);
        for (var i = 0; i < expected.Tabs.Count; i++)
        {
            Assert.Equal(expected.Tabs[i].Directory, actual.Tabs[i].Directory);
            Assert.Equal(expected.Tabs[i].Sort, actual.Tabs[i].Sort);
            Assert.Equal(expected.Tabs[i].ViewMode, actual.Tabs[i].ViewMode);
            Assert.Equal(expected.Tabs[i].ColumnWidths.Name, actual.Tabs[i].ColumnWidths.Name);
            Assert.Equal(expected.Tabs[i].ColumnWidths.Extension, actual.Tabs[i].ColumnWidths.Extension);
            Assert.Equal(expected.Tabs[i].ColumnWidths.Size, actual.Tabs[i].ColumnWidths.Size);
            Assert.Equal(expected.Tabs[i].ColumnWidths.Date, actual.Tabs[i].ColumnWidths.Date);
            Assert.Equal(expected.Tabs[i].ColumnWidths.Attributes, actual.Tabs[i].ColumnWidths.Attributes);
        }
    }

    private static Session SampleSession()
    {
        var session = new Session();
        session.RememberDirectory("/a");
        session.RememberDirectory("/b");
        session.Left.ActiveIndex = 1;
        session.Left.Tabs.Add(new SessionTab { Directory = "/left/one", ViewMode = PanelViewMode.Brief });
        session.Left.Tabs.Add(new SessionTab
        {
            Directory = "/left/two",
            Sort = new SortOrder(D3skCommander.Core.FileSystem.SortKey.Size, true),
            ViewMode = PanelViewMode.Thumbnails,
            ColumnWidths = new ColumnWidths { Name = 240f },
        });
        session.Right.Tabs.Add(new SessionTab { Directory = "/right", ViewMode = PanelViewMode.Full });
        session.NetShares.Add("smb://host/docs");
        session.ToolWindows.Add(new ToolWindowGeometry { Id = "search", X = 10f, Y = 20f, Width = 800f, Height = 600f });
        return session;
    }

    private static void AssertWidths(ColumnWidths widths)
    {
        Assert.Equal(Columns.NameMinimum, widths.Name);
        Assert.Equal(Columns.ExtensionWidth, widths.Extension);
        Assert.Equal(Columns.SizeWidth, widths.Size);
        Assert.Equal(Columns.DateWidth, widths.Date);
        Assert.Equal(Columns.AttributesWidth, widths.Attributes);
    }

    private static int Schema(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("schema").GetInt32();
    }

    private static string Prefs(Scratch dir) => Path.Combine(dir.Folder, SettingsFileNames.Preferences);

    private static string[] Names(string folder)
    {
        var names = Directory.GetFiles(folder).Select(Path.GetFileName).ToArray();
        Array.Sort(names!, StringComparer.Ordinal);
        return names!;
    }

    private static FakeTimeProvider Clock()
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2026, 11, 4, 18, 30, 45, TimeSpan.Zero));
        return clock;
    }

    private static void Settle(FakeTimeProvider clock) =>
        clock.Advance(TimeSpan.FromMilliseconds(SettingsStore<Preferences>.DebounceMilliseconds));

    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Folder = Path.Combine(Path.GetTempPath(), "d3sk-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
        }

        public string Folder { get; }

        public void Dispose()
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
    }
}
