using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Lister;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.QuickView;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.Smoke;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.App.Tests.Features.QuickView;

public class QuickViewTests
{
    [AvaloniaFact]
    public async Task CtrlQShowsTheFileInTheOtherPanelAndCtrlQAgainRestoresIt()
    {
        using var rig = await Rig.Open();
        var pane = rig.RightPane;
        Assert.False(pane.HasOverlay);

        rig.Toggle();
        await rig.Settle();

        Assert.True(rig.Commands.IsOn);
        Assert.Equal(PanelSide.Right, rig.Commands.Target);
        Assert.True(pane.HasOverlay);
        Assert.True(rig.Commands.Panel!.ShowsViewer);
        Assert.False(rig.LeftPane.HasOverlay);

        rig.Toggle();

        Assert.False(rig.Commands.IsOn);
        Assert.False(pane.HasOverlay);
    }

    [AvaloniaFact]
    public async Task ThePreviewFollowsTheCursorAfterThePause()
    {
        using var rig = await Rig.Open();
        rig.Toggle();
        await rig.Settle();
        Assert.Equal("alpha", rig.ShownFirstRow());

        rig.MoveCursorTo("b.txt");
        rig.Commands.Tick();
        Assert.Equal("alpha", rig.ShownFirstRow());

        rig.Commands.Tick();
        rig.Clock.Advance(QuickViewCommands.Debounce + TimeSpan.FromMilliseconds(10));
        rig.Commands.Tick();
        await rig.WaitFor(() => rig.ShownFirstRow() == "beta");

        Assert.Equal("beta", rig.ShownFirstRow());
    }

    [AvaloniaFact]
    public async Task AnImageShowsAsAnImageAndAFolderAsASummary()
    {
        using var rig = await Rig.Open();
        rig.Toggle();
        rig.MoveCursorTo("pixel.png");
        rig.Commands.Tick();
        rig.Clock.Advance(QuickViewCommands.Debounce + TimeSpan.FromMilliseconds(10));
        rig.Commands.Tick();
        await rig.WaitFor(() => rig.Viewer.Image is not null);
        Assert.NotNull(rig.Viewer.Image);

        rig.MoveCursorTo("sub");
        rig.Commands.Tick();
        rig.Clock.Advance(QuickViewCommands.Debounce + TimeSpan.FromMilliseconds(10));
        rig.Commands.Tick();
        await rig.WaitFor(() => rig.Commands.Panel!.SummaryText.Contains("items", StringComparison.Ordinal));

        Assert.False(rig.Commands.Panel!.ShowsViewer);
        Assert.Contains("sub", rig.Commands.Panel.SummaryText, StringComparison.Ordinal);
        Assert.Contains("2 items", rig.Commands.Panel.SummaryText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TheOtherPanelGettingTheFocusRestoresTheList()
    {
        using var rig = await Rig.Open();
        rig.Toggle();

        rig.Workspace.Activate(PanelSide.Right);
        rig.Commands.Tick();

        Assert.False(rig.Commands.IsOn);
        Assert.False(rig.RightPane.HasOverlay);
    }

    [Fact]
    public void FolderTextExistsInBothLanguages()
    {
        foreach (var language in new[] { Language.En, Language.Fr })
        {
            var text = Localizer.Load(settingsLanguage: language, systemLanguage: language);
            Assert.NotEqual("quickview.items", text.Get("quickview.items"));
            Assert.NotEqual("quickview.unreadable", text.Get("quickview.unreadable"));
        }
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _root;

        private Rig(string root, PanelWorkspace workspace)
        {
            _root = root;
            Workspace = workspace;
            Text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            Clock = new FakeTimeProvider();
            Commands = new QuickViewCommands(workspace, Text, new JobService(), Clock);
            var dialogs = new FakeDialogService();
            LeftPane = new PanelPaneModel(PanelSide.Left, workspace, Text, dialogs);
            RightPane = new PanelPaneModel(PanelSide.Right, workspace, Text, dialogs);
        }

        public PanelWorkspace Workspace { get; }

        public ILocalizer Text { get; }

        public FakeTimeProvider Clock { get; }

        public QuickViewCommands Commands { get; }

        public PanelPaneModel LeftPane { get; }

        public PanelPaneModel RightPane { get; }

        public ViewerViewModel Viewer =>
            (ViewerViewModel)((Avalonia.Controls.Control)Commands.Panel!.Content!).DataContext!;

        public static async Task<Rig> Open()
        {
            var root = Directory.CreateTempSubdirectory("d3sk-quick-").FullName;
            File.WriteAllText(Path.Combine(root, "a.txt"), "alpha\n");
            File.WriteAllText(Path.Combine(root, "b.txt"), "beta\n");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "images", "pixel.png"), Path.Combine(root, "pixel.png"));
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "sub", "x"), "x");
            File.WriteAllText(Path.Combine(root, "sub", "y"), "y");
            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new IdlePicker());
            await workspace.OpenAsync(root, root, TestContext.Current.CancellationToken);
            workspace.Activate(PanelSide.Left);
            var rig = new Rig(root, workspace);
            rig.MoveCursorTo("a.txt");
            return rig;
        }

        public void Toggle() => Commands.Toggle();

        public void MoveCursorTo(string name)
        {
            var snap = Workspace.Read(PanelSide.Left);
            for (var i = 0; i < snap.Entries.Count; i++)
            {
                if (snap.Entries[i].Name == name)
                {
                    Workspace.ApplySelection(PanelSide.Left, i, []);
                    return;
                }
            }

            throw new InvalidOperationException(name);
        }

        public string ShownFirstRow() => Viewer.Rows.Count > 0 ? Viewer.Rows[0].Text : "";

        public async Task WaitFor(Func<bool> done)
        {
            for (var i = 0; i < 300 && !done(); i++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
        }

        public Task Settle() => WaitFor(() => !Viewer.IsBusy && Viewer.Rows.Count > 0);

        public void Dispose()
        {
            Commands.Stop();
            Workspace.Dispose();
            Directory.Delete(_root, true);
        }
    }

    private sealed class IdlePicker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
