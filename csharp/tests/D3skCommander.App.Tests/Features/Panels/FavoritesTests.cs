using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Clipboard;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.App.Tests.Features.Panels;

public class FavoritesTests
{
    [AvaloniaFact]
    public async Task AFileFromTheContextTargetIsAddedAndPickingItRevealsItWithTheCursorOnIt()
    {
        using var rig = await Rig.Open("a.txt", "report.pdf");
        var file = rig.Of("report.pdf");

        rig.Dialogs.Enqueue(DialogResult.Ok("Report"));
        using (rig.Workspace.UseContextTarget(Target(ContextTargetKind.File, rig.Root, file)))
        {
            await rig.Commands.AddToFavoritesAsync();
        }

        var saved = Assert.Single(rig.Settings.Hotlist.Current.Items);
        Assert.Equal(("Report", file), (saved.Name, saved.Path));

        var elsewhere = Directory.CreateDirectory(Path.Combine(rig.Root, "elsewhere")).FullName;
        await rig.Workspace.GoAsync(PanelSide.Left, elsewhere, TestContext.Current.CancellationToken);
        rig.Dialogs.Enqueue(DialogResult.Picked(0));
        rig.Dialogs.Enqueue(DialogResult.Picked(0));
        await rig.Commands.HotlistAsync();

        var snap = rig.Workspace.Read(PanelSide.Left);
        Assert.Equal(rig.Root, snap.Path);
        Assert.Equal(file, snap.Entries[snap.Cursor].Path);
    }

    [AvaloniaFact]
    public async Task TheListLabelsFilesAndFolders()
    {
        using var rig = await Rig.Open("report.pdf");
        var folder = Directory.CreateDirectory(Path.Combine(rig.Root, "docs")).FullName;
        rig.Settings.Hotlist.Current.Add(new("Report", rig.Of("report.pdf")));
        rig.Settings.Hotlist.Current.Add(new("Docs", folder));

        rig.Dialogs.Enqueue(DialogResult.Cancel());
        await rig.Commands.HotlistAsync();

        Assert.Equal(["Report (file)  -  " + rig.Of("report.pdf"), "Docs (folder)  -  " + folder], (await rig.Dialogs.WaitShownAsync()).Choices);
    }

    [AvaloniaFact]
    public async Task AMissingFileOrFolderIsReportedAndRemovalIsOffered()
    {
        using var rig = await Rig.Open("report.pdf");
        var folder = Directory.CreateDirectory(Path.Combine(rig.Root, "docs")).FullName;
        rig.Settings.Hotlist.Current.Add(new("Report", rig.Of("report.pdf")));
        rig.Settings.Hotlist.Current.Add(new("Docs", folder));
        File.Delete(rig.Of("report.pdf"));
        Directory.Delete(folder);

        rig.Dialogs.Enqueue(DialogResult.Picked(0));
        rig.Dialogs.Enqueue(DialogResult.Cancel());
        await rig.Commands.HotlistAsync();
        await rig.Dialogs.WaitShownAsync();
        Assert.Equal("Not found: " + rig.Of("report.pdf"), (await rig.Dialogs.WaitShownAsync()).Text);
        Assert.Equal(2, rig.Settings.Hotlist.Current.Items.Count);

        rig.Dialogs.Enqueue(DialogResult.Picked(0));
        rig.Dialogs.Enqueue(DialogResult.Picked(0));
        await rig.Commands.HotlistAsync();
        Assert.Equal("Docs", Assert.Single(rig.Settings.Hotlist.Current.Items).Name);

        rig.Dialogs.Enqueue(DialogResult.Picked(0));
        rig.Dialogs.Enqueue(DialogResult.Picked(0));
        await rig.Commands.HotlistAsync();
        Assert.Empty(rig.Settings.Hotlist.Current.Items);
    }

    [AvaloniaFact]
    public async Task EmptySpaceAddsThePanelFolderAndARemoteTargetAddsNothing()
    {
        using var rig = await Rig.Open("a.txt");

        rig.Dialogs.Enqueue(DialogResult.Ok("Here"));
        using (rig.Workspace.UseContextTarget(Target(ContextTargetKind.EmptySpace, rig.Root, null)))
        {
            await rig.Commands.AddToFavoritesAsync();
        }

        Assert.Equal(rig.Root, Assert.Single(rig.Settings.Hotlist.Current.Items).Path);

        var shown = rig.Dialogs.ShownCount;
        using (rig.Workspace.UseContextTarget(Target(ContextTargetKind.File, rig.Root, rig.Of("a.txt")) with { Provider = PanelProviderKind.Remote }))
        {
            await rig.Commands.AddToFavoritesAsync();
        }

        Assert.Equal(shown, rig.Dialogs.ShownCount);
        Assert.Single(rig.Settings.Hotlist.Current.Items);
    }

    private static PanelContextTarget Target(ContextTargetKind kind, string folder, string? entry) =>
        new(kind, PanelProviderKind.Local, folder, entry, []);

    private sealed class Picker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoClipboard : IClipboardText
    {
        public Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class Rig : IDisposable
    {
        private Rig(string root, AppSettings settings, PanelWorkspace workspace, PanelCommands commands, FakeDialogService dialogs)
        {
            Root = root;
            Settings = settings;
            Workspace = workspace;
            Commands = commands;
            Dialogs = dialogs;
        }

        public string Root { get; }

        public AppSettings Settings { get; }

        public PanelWorkspace Workspace { get; }

        public PanelCommands Commands { get; }

        public FakeDialogService Dialogs { get; }

        public static async Task<Rig> Open(params string[] files)
        {
            var top = Directory.CreateTempSubdirectory("d3sk-fav-").FullName;
            var work = Directory.CreateDirectory(Path.Combine(top, "work")).FullName;
            foreach (var file in files)
            {
                await File.WriteAllTextAsync(Path.Combine(work, file), "x", TestContext.Current.CancellationToken);
            }

            var settings = new AppSettings(new SettingsFolder(Path.Combine(top, "cfg"), SettingsSource.Env));
            var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            var dialogs = new FakeDialogService();
            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker(), null, settings);
            await workspace.OpenAsync(work, work, TestContext.Current.CancellationToken);
            var commands = new PanelCommands(workspace, dialogs, text, new NoClipboard(), settings, new JobService());
            return new Rig(work, settings, workspace, commands, dialogs);
        }

        public string Of(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            Settings.Dispose();
            Directory.Delete(Path.GetDirectoryName(Root)!, true);
        }
    }
}
