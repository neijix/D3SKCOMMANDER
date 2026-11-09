using Avalonia;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Computer;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.Editor;
using D3skCommander.App.Features.Ftp;
using D3skCommander.App.Features.Launch;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.Search;
using D3skCommander.App.Features.WindowsMenu;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.Features.Ftp;
using D3skCommander.App.Tests.Features.Search;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Ftp;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Computer;

/// <summary>The commands that act on the panel folder, with the active panel at Computer: refused or empty, never a path built on the address.</summary>
public class ComputerCommandsTests
{
    [AvaloniaFact]
    public async Task EditNewShowsTheReadOnlyMessageBeforeAnyNameAndOpensNoEditor()
    {
        using var rig = await Rig.Open();
        var commands = rig.Services.GetRequiredService<EditorCommands>();
        var home = LocalFileSystem.HomeDirectory();
        var cwdBefore = Names(Environment.CurrentDirectory);
        var homeBefore = Names(home);

        var running = commands.EditNewAsync();
        var shown = await rig.Dialogs.WaitShownAsync();
        rig.Dialogs.Answer(DialogResult.Ok());
        await running;

        Assert.Equal(rig.Text.Get("computer.readonly"), shown.Text);
        Assert.Equal(1, rig.Dialogs.ShownCount);
        Assert.False(rig.Services.GetRequiredService<IToolWindowHost>().IsOpen(ToolKind.Editor));
        Assert.Equal(["usb"], Directory.GetFileSystemEntries(rig.Root).Select(path => Path.GetFileName(path)).Order().ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(rig.Root, "usb")));
        AssertNothingAdded(cwdBefore, Names(Environment.CurrentDirectory));
        AssertNothingAdded(homeBefore, Names(home));
    }

    private static HashSet<string> Names(string folder) =>
        Directory.GetFileSystemEntries(folder).Select(path => Path.GetFileName(path)).ToHashSet(StringComparer.Ordinal);

    private static void AssertNothingAdded(HashSet<string> before, HashSet<string> after)
    {
        var added = after.Where(name => !before.Contains(name)).ToList();
        Assert.Empty(added);
        Assert.DoesNotContain(after, name => name.StartsWith("computer", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task PropertiesOnTheEmptySpaceSaysNothingIsSelectedAndOnARowDescribesThatFolder()
    {
        using var rig = await Rig.Open();
        var commands = rig.Services.GetRequiredService<DrivesCommands>();

        var empty = rig.Panels.ContextTargetFor(PanelSide.Left, emptySpace: true)!;
        Task running;
        using (rig.Panels.UseContextTarget(empty))
        {
            running = commands.PropertiesAsync();
        }

        var none = await rig.Dialogs.WaitShownAsync();
        rig.Dialogs.Answer(DialogResult.Ok());
        await running;
        Assert.Equal(rig.Text.Get("props.none"), none.Text);

        running = commands.PropertiesAsync();
        var form = await rig.Dialogs.WaitShownAsync();
        rig.Dialogs.Answer(DialogResult.Ok());
        await running;
        Assert.NotNull(form.Form);
        Assert.Equal(2, rig.Dialogs.ShownCount);
    }

    [AvaloniaFact]
    public async Task AddToFavoritesAtComputerAsksNothingAndSavesNothing()
    {
        using var rig = await Rig.Open();
        var before = rig.Services.GetRequiredService<AppSettings>().Hotlist.Current.Items.Count;

        await rig.Services.GetRequiredService<PanelCommands>().AddToFavoritesAsync();

        Assert.Equal(0, rig.Dialogs.ShownCount);
        Assert.Equal(before, rig.Services.GetRequiredService<AppSettings>().Hotlist.Current.Items.Count);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TheTerminalAndPowerShellCommandsAtComputerShowThePanelUnavailableStatusAndStartNothing(bool elevated, bool powerShell)
    {
        using var rig = await Rig.Open();
        var commands = rig.Services.GetRequiredService<LaunchCommands>();
        commands.Platform = LaunchPlatform.Windows;
        var status = new List<string>();
        commands.ShowStatus = status.Add;

        await commands.OpenTerminalHereAsync(powerShell ? TerminalKind.PowerShell : TerminalKind.Terminal, elevated);

        Assert.Equal([rig.Text.Get("launch.panel_unavailable")], status);
        Assert.Equal(0, rig.Runner.Requests);
    }

    [AvaloniaFact]
    public async Task TheWindowsMenuAtComputerShowsThePanelUnavailableStatusAndOpensNoMenu()
    {
        using var rig = await Rig.Open();
        var module = rig.Services.GetRequiredService<WindowsMenuModule>();
        module.IsWindows = () => true;
        var shell = rig.Services.GetRequiredService<ShellContributions>();
        module.Contribute(shell);

        module.Show();

        Assert.Equal(rig.Text.Get("windowsmenu.panel_unavailable"), shell.Status);
        Assert.Equal(0, rig.Menu.Shown);
    }

    [AvaloniaFact]
    public async Task AShellLineAtComputerRunsInHomeAndKeepsThePrompt()
    {
        using var rig = await Rig.Open();
        var line = new CommandLineModel(rig.Panels, rig.Runner, rig.Dialogs, rig.Text) { Line = "echo hi" };

        line.Refresh();
        Assert.Equal("computer:>", line.Prompt);
        await line.RunAsync();

        var shell = Assert.IsType<ProcessRequest.Shell>(Assert.Single(rig.Runner.Started));
        Assert.Equal(LocalFileSystem.HomeDirectory(), shell.WorkingDirectory);
        Assert.Equal(ComputerAddress.Text, rig.Panels.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task CdRelativeAtComputerOpensUnderHomeAndCdComputerComesBack()
    {
        using var rig = await Rig.Open();
        var child = Directory.CreateDirectory(Path.Combine(LocalFileSystem.HomeDirectory(), "d3sk-computer-cd-" + Guid.NewGuid().ToString("N")));
        try
        {
            var line = new CommandLineModel(rig.Panels, rig.Runner, rig.Dialogs, rig.Text)
            {
                Line = "cd " + child.Name,
            };
            await line.RunAsync();

            Assert.Equal(child.FullName, rig.Panels.Read(PanelSide.Left).Path);
            Assert.Empty(rig.Runner.Started);

            line.Line = "cd COMPUTER:";
            await line.RunAsync();

            Assert.Equal(ComputerAddress.Text, rig.Panels.Read(PanelSide.Left).Path);
            Assert.Equal(0, rig.Dialogs.ShownCount);
        }
        finally
        {
            try
            {
                child.Delete(true);
            }
            catch (IOException)
            {
                // A cleanup failure must not mask the assertion that failed first.
            }
        }
    }

    [AvaloniaFact]
    public async Task AUserCommandWithTheActivePanelAtComputerStartsInHome()
    {
        using var rig = await Rig.Open();
        var capture = rig.Panels.Capture();

        Assert.NotNull(capture);
        Assert.Equal(ComputerAddress.Text, capture.ActiveDirectory);
        Assert.Equal(LocalFileSystem.HomeDirectory(), LaunchCommands.WorkingDirectory(capture));
        Assert.Equal(0, rig.Runner.Requests);
    }

    [AvaloniaFact]
    public async Task ADropOntoAPaneAtComputerShowsTheReadOnlyMessageAndCopiesNothing()
    {
        using var rig = await Rig.Open();
        var outside = Path.Combine(rig.Root, "dropped.txt");
        await File.WriteAllTextAsync(outside, "x", TestContext.Current.CancellationToken);
        var commands = rig.Services.GetRequiredService<DrivesCommands>();

        var running = commands.DropAsync(PanelSide.Left, [outside]);
        var shown = await rig.Dialogs.WaitShownAsync();
        rig.Dialogs.Answer(DialogResult.Ok());
        await running;

        Assert.Equal(rig.Text.Get("computer.readonly"), shown.Text);
        Assert.Equal(1, rig.Dialogs.ShownCount);
        Assert.Equal(["dropped.txt", "usb"], Directory.GetFileSystemEntries(rig.Root).Select(path => Path.GetFileName(path)).Order().ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(rig.Root, "usb")));
    }

    [AvaloniaFact]
    public async Task AnFtpDownloadWithTheActivePanelAtComputerIsRefusedBeforeAnyJobStarts()
    {
        using var rig = await Rig.Open();
        var dir = Directory.CreateTempSubdirectory("d3sk-computer-ftp-").FullName;
        try
        {
            using var store = new FtpSiteStore(dir);
            var jobs = new JobService();
            var model = new FtpViewModel(rig.Text, store, new FakeGateway(new FakeSession()), new FakeVault(), rig.Dialogs, jobs, rig.Panels);

            var running = model.DownloadAsync();
            var shown = await rig.Dialogs.WaitShownAsync();
            rig.Dialogs.Answer(DialogResult.Ok());
            var job = await running;

            Assert.Null(job);
            Assert.Equal(rig.Text.Get("computer.readonly"), shown.Text);
            Assert.Empty(jobs.Jobs);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public async Task SearchOpenedAtComputerPreparesHomeAsItsRoot()
    {
        using var rig = await Rig.Open();
        var model = rig.Services.GetRequiredService<SearchViewModel>();
        var commands = new SearchCommands(rig.Panels, new SearchTools(), model);

        commands.Open();

        Assert.Equal(LocalFileSystem.HomeDirectory(), model.Roots);
    }

    private sealed class RecordingRunner : D3skCommander.Core.Launch.IProcessRunner
    {
        public List<ProcessRequest> Started { get; } = [];

        public int Requests => Started.Count;

        public void Start(ProcessRequest request) => Started.Add(request);
    }

    private sealed class CountingMenu : IExplorerMenu
    {
        public int Shown { get; private set; }

        public ExplorerMenuResult Show(IReadOnlyList<string> paths, PixelPoint? screenPoint, IntPtr ownerWindow)
        {
            Shown++;
            return ExplorerMenuResult.Dismissed;
        }
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _root;

        private Rig(string root, ServiceProvider services, FakeDialogService dialogs, CountingMenu menu, RecordingRunner runner)
        {
            _root = root;
            Services = services;
            Dialogs = dialogs;
            Menu = menu;
            Runner = runner;
            Panels = services.GetRequiredService<PanelWorkspace>();
            Text = services.GetRequiredService<ILocalizer>();
        }

        public string Root => _root;

        public ServiceProvider Services { get; }

        public FakeDialogService Dialogs { get; }

        public CountingMenu Menu { get; }

        public RecordingRunner Runner { get; }

        public PanelWorkspace Panels { get; }

        public ILocalizer Text { get; }

        public static async Task<Rig> Open()
        {
            var root = Directory.CreateTempSubdirectory("d3sk-computer-cmd-").FullName;
            var mount = Directory.CreateDirectory(Path.Combine(root, "usb")).FullName;
            var dialogs = new FakeDialogService();
            var menu = new CountingMenu();
            var runner = new RecordingRunner();
            var services = TestComposition.Build(configure: s =>
            {
                s.AddSingleton<IComputerSources>(new OneDrive(mount));
                s.AddSingleton(new ComputerOptions([root]));
                s.AddSingleton<IDialogService>(dialogs);
                s.AddSingleton<IExplorerMenu>(menu);
                s.AddSingleton<D3skCommander.Core.Launch.IProcessRunner>(runner);
            });
            var rig = new Rig(root, services, dialogs, menu, runner);
            await rig.Panels.OpenAsync(mount, mount, TestContext.Current.CancellationToken);
            await rig.Panels.GoAsync(PanelSide.Left, ComputerAddress.Text, TestContext.Current.CancellationToken);
            return rig;
        }

        public void Dispose()
        {
            Panels.Dispose();
            Services.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
    }

    private sealed class OneDrive(string mount) : IComputerSources
    {
        public IReadOnlyList<DriveEntry> MountedDrives() => [new DriveEntry(mount, "USB", "ntfs", 1, 2)];

        public IReadOnlyList<ConnectedShare> ConnectedShares() => [];

        public IReadOnlyList<UserPlace> Places() => [];

        public IReadOnlyList<HotlistItem> Favorites() => [];

        public IReadOnlyList<string> MountPoints() => [mount];
    }
}
