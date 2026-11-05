using System.IO.Compression;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Clipboard;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.App.Tests.Features.Panels;

public class PanelCommandTests
{
    [AvaloniaFact]
    public async Task SelectGroupMarksByMaskAndUnselectGroupDropsThem()
    {
        using var rig = await Rig.Open("a.txt", "b.txt", "c.md");

        rig.Dialogs.Enqueue(DialogResult.Ok("*.txt"));
        await rig.Commands.SelectGroupAsync(true);
        Assert.Equal([rig.Of("a.txt"), rig.Of("b.txt")], Marked(rig));

        rig.Dialogs.Enqueue(DialogResult.Ok("a*"));
        await rig.Commands.SelectGroupAsync(false);
        Assert.Equal([rig.Of("b.txt")], Marked(rig));
    }

    [AvaloniaFact]
    public async Task SelectSameExtensionMarksFilesLikeTheCursorOne()
    {
        using var rig = await Rig.Open("a.txt", "b.txt", "c.md");
        rig.Cursor("b.txt");

        rig.Workspace.SelectSameExtension();

        Assert.Equal([rig.Of("a.txt"), rig.Of("b.txt")], Marked(rig));
    }

    [AvaloniaFact]
    public async Task CopyNamesAndPathsPutOneLinePerSelectedRowOnTheClipboard()
    {
        using var rig = await Rig.Open("a.txt", "b.txt", "c.md");
        rig.Workspace.SelectGroup("*.txt", true);

        await rig.Commands.CopyAsync(paths: false);
        Assert.Equal("a.txt\nb.txt", rig.Clipboard.Text);

        await rig.Commands.CopyAsync(paths: true);
        Assert.Equal(rig.Of("a.txt") + "\n" + rig.Of("b.txt"), rig.Clipboard.Text);
    }

    [AvaloniaFact]
    public async Task AddHotlistSavesTheFolderGoToOpensItAndRemoveDropsIt()
    {
        using var rig = await Rig.Open("a.txt");
        var other = Directory.CreateDirectory(Path.Combine(rig.Root, "other")).FullName;
        await rig.Workspace.GoAsync(PanelSide.Left, other, TestContext.Current.CancellationToken);

        rig.Dialogs.Enqueue(DialogResult.Ok("My other"));
        await rig.Commands.AddHotlistAsync();
        var saved = Assert.Single(rig.Settings.Hotlist.Current.Items);
        Assert.Equal(("My other", other), (saved.Name, saved.Path));
        rig.Settings.Flush();
        Assert.Single(new HotlistStore(rig.Settings.Directory).Current.Items);

        await rig.Workspace.GoAsync(PanelSide.Left, rig.Root, TestContext.Current.CancellationToken);
        rig.Dialogs.Enqueue(DialogResult.Picked(0));
        rig.Dialogs.Enqueue(DialogResult.Picked(0));
        await rig.Commands.HotlistAsync();
        Assert.Equal(other, rig.Workspace.Read(PanelSide.Left).Path);

        rig.Dialogs.Enqueue(DialogResult.Picked(0));
        rig.Dialogs.Enqueue(DialogResult.Picked(1));
        await rig.Commands.HotlistAsync();
        Assert.Empty(rig.Settings.Hotlist.Current.Items);
    }

    [AvaloniaFact]
    public async Task VisitedFoldersAreRecordedInTheSessionAndHistoryOpensOne()
    {
        using var rig = await Rig.Open("a.txt");
        var sub = Directory.CreateDirectory(Path.Combine(rig.Root, "sub")).FullName;
        await rig.Workspace.GoAsync(PanelSide.Left, sub, TestContext.Current.CancellationToken);
        Assert.Equal(sub, rig.Settings.Session.Current.History[0]);
        rig.Settings.Flush();
        Assert.Equal(sub, new SessionStore(rig.Settings.Directory).Current.History[0]);

        await rig.Workspace.GoAsync(PanelSide.Left, rig.Root, TestContext.Current.CancellationToken);
        rig.Dialogs.Enqueue(DialogResult.Picked(1));
        await rig.Commands.HistoryAsync();

        Assert.Equal(sub, rig.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task BranchListsNestedFilesAndASecondRunLeavesIt()
    {
        using var rig = await Rig.Open("top.txt");
        Directory.CreateDirectory(Path.Combine(rig.Root, "sub"));
        await File.WriteAllTextAsync(Path.Combine(rig.Root, "sub", "in.txt"), "x", TestContext.Current.CancellationToken);
        await rig.Workspace.RefreshAsync(TestContext.Current.CancellationToken);

        await rig.Workspace.ToggleBranchAsync(TestContext.Current.CancellationToken);
        var names = rig.Workspace.Read(PanelSide.Left).Entries.Select(entry => entry.Name).ToList();
        Assert.Contains(Path.Combine("sub", "in.txt"), names);
        Assert.DoesNotContain("sub", names);

        await rig.Workspace.ToggleBranchAsync(TestContext.Current.CancellationToken);
        Assert.Contains("sub", rig.Workspace.Read(PanelSide.Left).Entries.Select(entry => entry.Name));
    }

    [AvaloniaFact]
    public async Task DirSizesFillsTheSizeOfTheFolders()
    {
        using var rig = await Rig.Open("a.txt");
        var sub = Directory.CreateDirectory(Path.Combine(rig.Root, "sub")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(sub, "big.bin"), new byte[3000], TestContext.Current.CancellationToken);
        await rig.Workspace.RefreshAsync(TestContext.Current.CancellationToken);

        await rig.Commands.DirSizesAsync();

        var folder = rig.Workspace.Read(PanelSide.Left).Entries.Single(entry => entry.Name == "sub");
        Assert.Equal(3000UL, folder.Size);
    }

    [AvaloniaFact]
    public async Task QuickFilterNarrowsAsYouTypeAndEscRestoresTheList()
    {
        using var rig = await Rig.Open("alpha.txt", "beta.txt", "gamma.md");
        var pane = new PanelPaneModel(PanelSide.Left, rig.Workspace, rig.Text, rig.Dialogs);
        pane.Tick();

        rig.Workspace.OpenFilter();
        pane.Tick();
        Assert.True(pane.FilterOpen);

        pane.FilterText = "ta";
        var shown = rig.Workspace.Read(PanelSide.Left).Entries.Where(entry => !entry.IsParent).Select(entry => entry.Name);
        Assert.Equal(["beta.txt"], shown);

        pane.CloseFilter();
        pane.Tick();
        Assert.False(pane.FilterOpen);
        Assert.Equal(3, rig.Workspace.Read(PanelSide.Left).Entries.Count(entry => !entry.IsParent));
    }

    [AvaloniaFact]
    public async Task ToggleHiddenSavesThePreferenceAndTheNextStartReadsIt()
    {
        using var rig = await Rig.Open("a.txt", ".secret");
        Assert.DoesNotContain(".secret", rig.Workspace.Read(PanelSide.Left).Entries.Select(entry => entry.Name));

        await rig.Workspace.ToggleHiddenAsync(TestContext.Current.CancellationToken);
        Assert.True(rig.Settings.Preferences.Current.ShowHidden);

        var next = new PanelWorkspace(new LocalFileSystemProvider(), new Picker(), null, rig.Settings);
        await next.OpenAsync(rig.Root, rig.Root, TestContext.Current.CancellationToken);
        Assert.Contains(".secret", next.Read(PanelSide.Left).Entries.Select(entry => entry.Name));
    }

    [AvaloniaFact]
    public async Task EnterOnAnArchiveOpensItLikeAFolder()
    {
        using var rig = await Rig.Open("a.txt");
        var zip = Path.Combine(rig.Root, "pack.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("inside.txt");
            await using var stream = entry.Open();
            await stream.WriteAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
        }

        await rig.Workspace.RefreshAsync(TestContext.Current.CancellationToken);
        rig.Cursor("pack.zip");
        await rig.Workspace.EnterAsync(TestContext.Current.CancellationToken);

        var snap = rig.Workspace.Read(PanelSide.Left);
        Assert.Equal(zip, snap.Path);
        Assert.Contains("inside.txt", snap.Entries.Select(entry => entry.Name));
    }

    private static List<string> Marked(Rig rig) => [.. rig.Workspace.Read(PanelSide.Left).Marked.Order(StringComparer.Ordinal)];

    private sealed class Picker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeClipboard : IClipboardText
    {
        public string? Text { get; private set; }

        public Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.FromResult(true);
        }
    }

    private sealed class Rig : IDisposable
    {
        private Rig(string root, AppSettings settings, PanelWorkspace workspace, PanelCommands commands, FakeDialogService dialogs, FakeClipboard clipboard, ILocalizer text)
        {
            Root = root;
            Settings = settings;
            Workspace = workspace;
            Commands = commands;
            Dialogs = dialogs;
            Clipboard = clipboard;
            Text = text;
        }

        public string Root { get; }

        public AppSettings Settings { get; }

        public PanelWorkspace Workspace { get; }

        public PanelCommands Commands { get; }

        public FakeDialogService Dialogs { get; }

        public FakeClipboard Clipboard { get; }

        public ILocalizer Text { get; }

        public static async Task<Rig> Open(params string[] files)
        {
            var root = Directory.CreateTempSubdirectory("d3sk-pcmd-").FullName;
            var work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
            foreach (var file in files)
            {
                await System.IO.File.WriteAllTextAsync(Path.Combine(work, file), "x", TestContext.Current.CancellationToken);
            }

            var settings = new AppSettings(new SettingsFolder(Path.Combine(root, "cfg"), SettingsSource.Env));
            var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            var dialogs = new FakeDialogService();
            var clipboard = new FakeClipboard();
            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker(), null, settings);
            await workspace.OpenAsync(work, work, TestContext.Current.CancellationToken);
            var commands = new PanelCommands(workspace, dialogs, text, clipboard, settings, new JobService());
            return new Rig(work, settings, workspace, commands, dialogs, clipboard, text);
        }

        public string Of(string name) => Path.Combine(Root, name);

        public void Cursor(string name)
        {
            var snap = Workspace.Read(PanelSide.Left);
            var index = snap.Entries.ToList().FindIndex(entry => entry.Name == name);
            Workspace.ApplySelection(PanelSide.Left, index, []);
        }

        public void Dispose()
        {
            Settings.Dispose();
            Directory.Delete(Path.GetDirectoryName(Root)!, true);
        }
    }
}
