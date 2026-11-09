using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Computer;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Computer;

public class ComputerViewTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [AvaloniaFact]
    public async Task UpFromADriveGoesToComputerWithTheCursorOnItAndOpeningTheRowComesBack()
    {
        using var rig = await Rig.Open();

        await rig.Panels.ParentAsync(TestContext.Current.CancellationToken);

        var computer = rig.Panels.Read(PanelSide.Left);
        Assert.Equal(ComputerAddress.Text, computer.Path);
        Assert.DoesNotContain(computer.Entries, entry => entry.IsParent);
        Assert.Equal(rig.Mount, computer.Entries[computer.Cursor].Path);

        await rig.Panels.EnterAsync(TestContext.Current.CancellationToken);
        Assert.Equal(rig.Mount, rig.Panels.Read(PanelSide.Left).Path);

        await rig.Panels.ParentAsync(TestContext.Current.CancellationToken);
        var again = rig.Panels.Read(PanelSide.Left);
        Assert.Equal(ComputerAddress.Text, again.Path);
        Assert.Equal(rig.Mount, again.Entries[again.Cursor].Path);
    }

    [AvaloniaFact]
    public async Task ThePathBarTakesTheAddressInAnyCaseAndARelativeNameResolvesUnderHome()
    {
        using var rig = await Rig.Open();

        await rig.Panels.GoAsync(PanelSide.Left, "  CoMpUtEr: ", TestContext.Current.CancellationToken);

        Assert.Equal(ComputerAddress.Text, rig.Panels.Read(PanelSide.Left).Path);
        Assert.Equal(Path.Combine(LocalFileSystem.HomeDirectory(), "Documents"), PanelWorkspace.Resolve(ComputerAddress.Text, "Documents"));
        Assert.Equal(ComputerAddress.Text, PanelWorkspace.Resolve(rig.Mount, "Computer:"));
        Assert.Equal(Path.Combine(rig.Mount, "x"), PanelWorkspace.Resolve(rig.Mount, "x"));
    }

    [AvaloniaFact]
    public async Task TheComputerCommandOpensItInTheActivePanel()
    {
        using var rig = await Rig.Open();
        var window = rig.Services.GetRequiredService<MainWindow>();

        Assert.True(window.Dispatch(CommandId.Computer));

        await Until(() => rig.Panels.Read(PanelSide.Left).Path == ComputerAddress.Text);
        Assert.Equal(rig.Mount, rig.Panels.Read(PanelSide.Right).Path);
    }

    [AvaloniaFact]
    public async Task RefreshReadsTheSourcesAgainButThePollDoesNot()
    {
        using var rig = await Rig.Open();
        await rig.Panels.GoAsync(PanelSide.Left, ComputerAddress.Text, TestContext.Current.CancellationToken);
        var reads = rig.Sources.Reads;

        await rig.Panels.PollAsync(TestContext.Current.CancellationToken);
        Assert.Equal(reads, rig.Sources.Reads);

        await rig.Panels.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(rig.Sources.Reads > reads);
    }

    [AvaloniaFact]
    public async Task TheTreeKeepsItsLastFolderAtComputerAndTheContextMenuIsTheRemoteOne()
    {
        using var rig = await Rig.Open();
        var pane = new PanelPaneModel(
            PanelSide.Left, rig.Panels, rig.Services.GetRequiredService<ILocalizer>(), rig.Services.GetRequiredService<IDialogService>(),
            null, new FakeDrives(new DriveEntry(rig.Mount, "USB", "ntfs", 1, 2))); // a tree root that holds the mount even when temp is outside home
        // The tree choice lives in the settings folder the other tests share: turn it on only when it is off.
        var turnedOn = !rig.Panels.ShowTree(PanelSide.Left);
        if (turnedOn)
        {
            rig.Panels.ToggleTree(PanelSide.Left);
        }

        try
        {
            pane.Tick();
            await Until(() => pane.Tree.Selected is not null);
            var selected = pane.Tree.Selected;

            await rig.Panels.ParentAsync(TestContext.Current.CancellationToken);
            pane.Tick();
            await Task.Delay(100, TestContext.Current.CancellationToken);

            Assert.Same(selected, pane.Tree.Selected);
            var empty = rig.Panels.ContextTargetFor(PanelSide.Left, emptySpace: true)!;
            Assert.Equal(PanelProviderKind.Remote, empty.Provider);
            var row = rig.Panels.ContextTargetFor(PanelSide.Left, emptySpace: false)!;
            Assert.Equal(PanelProviderKind.Remote, row.Provider);
            var entries = PanelContextMenu.Build(empty, MenuPlatform.Windows, clipboardHasFiles: true, pwshFound: true);
            Assert.Contains(entries, entry => entry.Command == CommandId.MkDir);
            Assert.DoesNotContain(entries, entry => entry.Command is CommandId.TerminalHere or CommandId.AddToFavorites or CommandId.EditNew);
        }
        finally
        {
            if (turnedOn)
            {
                rig.Panels.ToggleTree(PanelSide.Left);
            }
        }
    }

    [AvaloniaFact]
    public async Task TheParentRowTargetIsItsOwnPathSoAMountOpensComputerNotItsParentFolder()
    {
        using var rig = await Rig.Open();
        var snap = rig.Panels.Read(PanelSide.Left);
        var parentRow = snap.Entries.Single(entry => entry.IsParent);
        rig.Panels.ApplySelection(PanelSide.Left, snap.Entries.ToList().IndexOf(parentRow), []);

        var target = rig.Panels.ContextTargetFor(PanelSide.Left, emptySpace: false)!;
        Assert.Equal(ComputerAddress.Text, target.EntryPath);

        using (rig.Panels.UseContextTarget(target))
        {
            await rig.Panels.OpenInOtherPanelAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(ComputerAddress.Text, rig.Panels.Read(PanelSide.Right).Path);
    }

    private static async Task Until(Func<bool> done)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!done() && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(done());
    }

    // A temp folder stands in for a removable mount: its parent is the only prefix and it is the only drive and mount point.
    private sealed class Rig : IDisposable
    {
        private readonly string _root;

        private Rig(string root, ServiceProvider services, CountingSources sources, string mount)
        {
            _root = root;
            Services = services;
            Sources = sources;
            Mount = mount;
            Panels = services.GetRequiredService<PanelWorkspace>();
        }

        public ServiceProvider Services { get; }

        public CountingSources Sources { get; }

        public string Mount { get; }

        public PanelWorkspace Panels { get; }

        public static async Task<Rig> Open()
        {
            var root = Directory.CreateTempSubdirectory("d3sk-computer-").FullName;
            var mount = Directory.CreateDirectory(Path.Combine(root, "usb")).FullName;
            var sources = new CountingSources(mount);
            var services = TestComposition.Build(configure: s =>
            {
                s.AddSingleton<IComputerSources>(sources);
                s.AddSingleton(new ComputerOptions([root]));
            });
            var rig = new Rig(root, services, sources, mount);
            await rig.Panels.OpenAsync(mount, mount, TestContext.Current.CancellationToken);
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

    private sealed class FakeDrives(params DriveEntry[] list) : IDriveSource
    {
        public IReadOnlyList<DriveEntry> Mounted() => list;
    }

    private sealed class CountingSources(string mount) : IComputerSources
    {
        public int Reads { get; private set; }

        public IReadOnlyList<DriveEntry> MountedDrives()
        {
            Reads++;
            return [new DriveEntry(mount, "USB", "ntfs", 1, 2)];
        }

        public IReadOnlyList<ConnectedShare> ConnectedShares() => [];

        public IReadOnlyList<UserPlace> Places() => [];

        public IReadOnlyList<HotlistItem> Favorites() => [];

        public IReadOnlyList<string> MountPoints() => [mount];
    }
}
