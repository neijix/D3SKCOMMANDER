using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Clipboard;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.App.Tests.Features.Panels;

public class PanelSizesStatusTests
{
    [AvaloniaFact]
    public async Task SpaceOnAFolderMarksItAndMeasuresItAndTheStatusShowsTheSize()
    {
        using var rig = await Rig.Open();

        rig.Pane.List.CursorIndex = rig.Index("sub");
        rig.Key(Key.Space);

        Assert.True(rig.Pane.List.IsMarked(Path.Combine(rig.Work, "sub")));
        await rig.WaitUntil(() => rig.Workspace.Read(PanelSide.Left).Entries.Single(e => e.Name == "sub").Size == 3000UL);
        rig.Pane.Tick();
        Assert.StartsWith("1 / 2 selected · 2.9 KB / 2.9 KB", rig.Pane.StatusText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AFolderMeasuredBeforeItsMarkArrivesCountsInTheMarkedSize()
    {
        using var rig = await Rig.Open();
        var sub = Path.Combine(rig.Work, "sub");

        await rig.Workspace.ApplyFolderSizeAsync(PanelSide.Left, rig.Work, sub, 3000UL, TestContext.Current.CancellationToken);
        rig.Workspace.ApplySelection(PanelSide.Left, rig.Index("sub"), [sub]);

        Assert.Equal(3000UL, rig.Workspace.Read(PanelSide.Left).SelectedBytes);
        rig.Pane.Tick();
        Assert.StartsWith("1 / 2 selected · 2.9 KB / 2.9 KB", rig.Pane.StatusText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task WithAutoFolderSizesOnAFolderSizeAppearsWithoutAKeyPress()
    {
        using var rig = await Rig.Open(autoSizes: true);

        await rig.WaitUntil(() => rig.Workspace.Read(PanelSide.Left).Entries.Single(e => e.Name == "sub").Size == 3000UL);

        Assert.Equal(3000UL, rig.Workspace.SizeCache.Get(Path.Combine(rig.Work, "sub"), Directory.GetLastWriteTimeUtc(Path.Combine(rig.Work, "sub"))));
    }

    [AvaloniaFact]
    public async Task WithAutoFolderSizesOffNothingIsMeasured()
    {
        using var rig = await Rig.Open();

        await Task.Delay(500, TestContext.Current.CancellationToken);
        await rig.Workspace.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0UL, rig.Workspace.Read(PanelSide.Left).Entries.Single(e => e.Name == "sub").Size);
        Assert.Equal(0, rig.Workspace.SizeCache.Count);
    }

    [AvaloniaFact]
    public async Task SpaceOnTheParentRowMeasuresNothing()
    {
        using var rig = await Rig.Open();
        var requests = 0;
        rig.Workspace.FolderSizeRequested += (_, _) => requests++;

        rig.Pane.List.CursorIndex = 0;
        rig.Key(Key.Space);
        rig.Pane.List.CursorIndex = rig.Index("a.txt");
        rig.Key(Key.Space);

        Assert.Equal(0, requests);
    }

    [AvaloniaFact]
    public async Task TheStatusOfALocalFolderEndsWithTheFreeSpace()
    {
        using var rig = await Rig.Open();

        await rig.WaitUntil(() =>
        {
            rig.Pane.Tick();
            return rig.Pane.StatusText.EndsWith(" free", StringComparison.Ordinal);
        });

        Assert.Matches(@"^0 / 2 selected · 0 B / 0 B · .+ free$", rig.Pane.StatusText);
    }

    private sealed class Rig : IDisposable
    {
        private readonly Window _window;
        private readonly AppSettings _settings;
        private readonly string _root;

        private Rig(string root, string work, AppSettings settings, PanelWorkspace workspace, PanelPaneModel pane, Window window)
        {
            _root = root;
            Work = work;
            _settings = settings;
            Workspace = workspace;
            Pane = pane;
            _window = window;
        }

        public string Work { get; }

        public PanelWorkspace Workspace { get; }

        public PanelPaneModel Pane { get; }

        public static async Task<Rig> Open(bool autoSizes = false)
        {
            var token = TestContext.Current.CancellationToken;
            var root = Directory.CreateTempSubdirectory("d3sk-psize-").FullName;
            var work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
            await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "", token);
            var sub = Directory.CreateDirectory(Path.Combine(work, "sub")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(sub, "big.bin"), new byte[3000], token);
            var settings = new AppSettings(new SettingsFolder(Path.Combine(root, "cfg"), SettingsSource.Env));
            if (autoSizes)
            {
                var preferences = settings.Preferences.Current;
                preferences.AutoFolderSizes = true;
                settings.Preferences.Save(preferences);
            }

            var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            var dialogs = new FakeDialogService();
            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Picker(), null, settings);
            _ = new PanelCommands(workspace, dialogs, text, new Clipboard(), settings, new JobService());
            await workspace.OpenAsync(work, work, token);
            var pane = new PanelPaneModel(PanelSide.Left, workspace, text, dialogs);
            var window = new Window { Width = 900, Height = 400, Content = pane.CreateContent() };
            var rig = new Rig(root, work, settings, workspace, pane, window);
            window.Show();
            rig.Settle();
            await rig.WaitUntil(() => pane.List.CellText(0, 0) is not null);
            ((Control)window.Content!).Focus();
            return rig;
        }

        public int Index(string name) => Workspace.Read(PanelSide.Left).Entries.ToList().FindIndex(e => e.Name == name);

        public void Key(Key key)
        {
            _window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            Settle();
        }

        public void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            _window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public async Task WaitUntil(Func<bool> ready)
        {
            var start = DateTime.UtcNow;
            while (!ready())
            {
                if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5))
                {
                    throw new TimeoutException("The pane did not update.");
                }

                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        public void Dispose()
        {
            _window.Close();
            Workspace.Dispose();
            _settings.Dispose();
            Directory.Delete(_root, true);
        }
    }

    private sealed class Picker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class Clipboard : IClipboardText
    {
        public Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
