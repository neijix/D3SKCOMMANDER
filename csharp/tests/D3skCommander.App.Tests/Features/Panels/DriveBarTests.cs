using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Xunit;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Features.Panels;

public class DriveBarTests
{
    [AvaloniaFact]
    public async Task TheBarListsTheDrivesAndHighlightsTheOneHoldingTheFolder()
    {
        using var rig = await Rig.Opened();

        var buttons = rig.View.DriveButtons;
        Assert.Equal(2, buttons.Count);
        Assert.Equal(rig.FolderB, (string?)buttons[1].Tag);
        Assert.Contains("held", buttons[0].Classes);
        Assert.DoesNotContain("held", buttons[1].Classes);
        Assert.Contains(rig.FolderA, ToolTip.GetTip(buttons[0])?.ToString());
        Assert.Contains("free of", ToolTip.GetTip(buttons[0])?.ToString());
    }

    [AvaloniaFact]
    public async Task AClickGoesToTheDriveAndTheHighlightFollows()
    {
        using var rig = await Rig.Opened();

        rig.View.DriveButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await rig.WaitUntil(() => rig.Workspace.Read(PanelSide.Left).Path == rig.FolderB);
        await rig.WaitUntil(() => rig.View.DriveButtons[1].Classes.Contains("held"));

        Assert.DoesNotContain("held", rig.View.DriveButtons[0].Classes);
    }

    [AvaloniaFact]
    public async Task ANewTabOpensTheDriveInThatPanel()
    {
        using var rig = await Rig.Opened();

        await rig.Workspace.NewTabAtAsync(PanelSide.Left, rig.FolderB, TestContext.Current.CancellationToken);

        Assert.Equal(rig.FolderB, rig.Workspace.Read(PanelSide.Left).Path);
        rig.Workspace.CloseTab();
        Assert.Equal(rig.FolderA, rig.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task TheCommandHidesAndShowsTheBar()
    {
        using var rig = await Rig.Opened();
        Assert.True(rig.Scroll.IsVisible);

        rig.Workspace.ToggleDriveBar();
        await rig.WaitUntil(() => !rig.Scroll.IsVisible);

        rig.Workspace.ToggleDriveBar();
        await rig.WaitUntil(() => rig.Scroll.IsVisible);
    }

    [Fact]
    public async Task TheFavoritesFollowTheDrivesByName()
    {
        var bar = new DriveBarModel(new FakeDrives(new DriveEntry("/a", "Alpha", "ext4", 10, 100)), TextCatalog.Load(Language.En, Language.En));
        List<HotlistItem> favorites = [new("Projects", "/a/projects"), new("", "/a/music/")];
        for (var i = 0; i < 200 && bar.Buttons.Count < 3; i++)
        {
            bar.Update("/a", favorites);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(["/a", "/a/projects", "/a/music/"], bar.Buttons.Select(b => b.Path));
        Assert.Equal([false, true, true], bar.Buttons.Select(b => b.Favorite));
        Assert.Equal("Projects", bar.Buttons[1].Text);
        Assert.Equal("music", bar.Buttons[2].Text);

        favorites[0].Name = "Work";
        Assert.True(bar.Update("/a", favorites));
        Assert.Equal("Work", bar.Buttons[1].Text);
        Assert.False(bar.Update("/a", favorites));
    }

    private sealed class FakeDrives(params DriveEntry[] list) : IDriveSource
    {
        public IReadOnlyList<DriveEntry> Mounted() => list;
    }

    private sealed class Picker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<string?> PickFolderAsync(string title, string? start, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class Rig : IDisposable
    {
        private readonly Window _window;

        private Rig(string a, string b)
        {
            FolderA = a;
            FolderB = b;
            var text = TextCatalog.Load(Language.En, Language.En);
            Workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker());
            var drives = new FakeDrives(new DriveEntry(a, "Alpha", "ext4", 10, 100), new DriveEntry(b, "Beta", "ext4", 20, 200));
            Pane = new PanelPaneModel(PanelSide.Left, Workspace, text, new FakeDialogService(), null, drives);
            View = (PanelPaneView)Pane.CreateContent();
            _window = new Window { Width = 800, Height = 400, Content = View };
        }

        public string FolderA { get; }

        public string FolderB { get; }

        public PanelWorkspace Workspace { get; }

        public PanelPaneModel Pane { get; }

        public PanelPaneView View { get; }

        public ScrollViewer Scroll => View.FindControl<ScrollViewer>("DriveScroll")!;

        public static async Task<Rig> Opened()
        {
            var a = Directory.CreateTempSubdirectory("d3sk-drive-a-").FullName;
            var b = Directory.CreateTempSubdirectory("d3sk-drive-b-").FullName;
            var rig = new Rig(a, b);
            await rig.Workspace.OpenAsync(a, a, TestContext.Current.CancellationToken);
            rig._window.Show();
            await rig.WaitUntil(() => rig.View.DriveButtons.Count == 2);
            return rig;
        }

        public async Task WaitUntil(Func<bool> done)
        {
            for (var i = 0; i < 200 && !done(); i++)
            {
                Pane.Tick();
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(done());
        }

        public void Dispose()
        {
            _window.Close();
            Workspace.Dispose();
            Directory.Delete(FolderA, true);
            Directory.Delete(FolderB, true);
        }
    }
}
