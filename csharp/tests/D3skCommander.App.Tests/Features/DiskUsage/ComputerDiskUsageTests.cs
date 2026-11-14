using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.DiskUsage;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.DiskUsage;

public class ComputerDiskUsageTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    [AvaloniaFact]
    public async Task AtComputerWithNoDriveRowTheWindowScansEveryDriveLeavingOutTheMountPoints()
    {
        using var rig = new Rig();
        using var host = rig.Host(rig.Panels(cursorOnDrive: null));

        host.Window.Dispatch(CommandId.DiskUsage);
        var model = host.Provider.GetRequiredService<DiskUsageViewModel>();
        await ScanOf(model);

        Assert.Equal(ComputerAddress.Text, model.RootPath);
        Assert.Equal(host.Localizer.Get("computer.title"), model.Root!.Name);
        Assert.Equal(ComputerAddress.Text, model.Root.Path);
        Assert.Equal(2, model.Rows.Count);
        // Drive A holds 1000 bytes of its own and a 700 byte mount point that is skipped.
        Assert.Equal(1000 + 300, model.Root.Bytes);
    }

    [AvaloniaFact]
    public async Task TheSourcesAreReadOffTheUiThread()
    {
        using var rig = new Rig();
        using var host = rig.Host(rig.Panels(cursorOnDrive: null));
        var uiThread = Environment.CurrentManagedThreadId;

        rig.Sources.Threads.Clear();
        host.Window.Dispatch(CommandId.DiskUsage);
        var model = host.Provider.GetRequiredService<DiskUsageViewModel>();
        await ScanOf(model);

        Assert.NotEmpty(rig.Sources.Threads);
        Assert.DoesNotContain(uiThread, rig.Sources.Threads);
    }

    [AvaloniaFact]
    public async Task ADriveRowScansThatFolderAsToday()
    {
        using var rig = new Rig();
        using var host = rig.Host(rig.Panels(cursorOnDrive: rig.DriveB));

        host.Window.Dispatch(CommandId.DiskUsage);
        var model = host.Provider.GetRequiredService<DiskUsageViewModel>();
        await ScanOf(model);

        Assert.Equal(rig.DriveB, model.RootPath);
        Assert.Equal(300, model.Root!.Bytes);
    }

    [AvaloniaFact]
    public async Task RescanRepeatsTheDrivesScanAndShowInPanelOnTheRootOpensComputer()
    {
        using var rig = new Rig();
        var panels = rig.Panels(cursorOnDrive: null);
        using var host = rig.Host(panels);
        host.Window.Dispatch(CommandId.DiskUsage);
        var model = host.Provider.GetRequiredService<DiskUsageViewModel>();
        await ScanOf(model);

        File.WriteAllBytes(Path.Combine(rig.DriveB, "more.bin"), new byte[50]);
        await model.RescanAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal(ComputerAddress.Text, model.RootPath);
        Assert.Equal(1000 + 350, model.Root!.Bytes);

        model.Selected = null;
        Assert.True(await model.ShowInPanelAsync());
        Assert.Equal([ComputerAddress.Text], panels.Went);
        Assert.Empty(panels.Revealed);
    }

    [AvaloniaFact]
    public async Task AFavoriteRowScansThatFavoriteNotTheDrives()
    {
        using var rig = new Rig();
        using var host = rig.Host(rig.Panels(cursorOnDrive: rig.Favorite));

        host.Window.Dispatch(CommandId.DiskUsage);
        var model = host.Provider.GetRequiredService<DiskUsageViewModel>();
        await ScanOf(model);

        Assert.Equal(rig.Favorite, model.RootPath);
        Assert.Equal(40, model.Root!.Bytes);
    }

    [AvaloniaFact]
    public async Task AServerWhoseSharesCannotBeListedSaysSoAndScansNothing()
    {
        using var rig = new Rig();
        var dialogs = new FakeDialogService();
        var servers = new FailingServers();
        var panels = new ComputerPanels([], -1, "\\\\192.168.1.15\\");
        using var host = rig.Host(panels, servers, dialogs);

        host.Window.Dispatch(CommandId.DiskUsage);
        var shown = await dialogs.WaitShownAsync().AsTask().WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(["192.168.1.15"], servers.Asked);
        Assert.Contains("\\\\192.168.1.15\\", shown.Text, StringComparison.Ordinal);
        Assert.Null(host.Provider.GetRequiredService<DiskUsageViewModel>().RootPath);
        dialogs.Answer(DialogResult.Ok());
    }

    [AvaloniaFact]
    public async Task TheSharesOfAServerAreScannedUnderOneRootNamedAfterIt()
    {
        using var rig = new Rig();
        using var host = rig.Host(rig.Panels(cursorOnDrive: null));
        var model = host.Provider.GetRequiredService<DiskUsageViewModel>();

        await model.BeginSharesAsync("\\\\nas\\", [rig.DriveB, rig.Favorite]).WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal("\\\\nas\\", model.RootPath);
        Assert.Equal("\\\\nas\\", model.Root!.Name);
        Assert.Equal(2, model.Rows.Count);
        Assert.Equal(300 + 40, model.Root.Bytes);
    }

    // The command reads its sources on a worker first, so the scan starts a moment after the dispatch.
    private static async Task ScanOf(DiskUsageViewModel model)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (ReferenceEquals(model.ScanTask, Task.CompletedTask) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await model.ScanTask.WaitAsync(Wait, TestContext.Current.CancellationToken);
    }

    // A: 1000 bytes plus mount/ (700 bytes, a mount point); B: 300 bytes.
    private sealed class Rig : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("d3sk-du-computer-").FullName;

        public Rig()
        {
            DriveA = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
            DriveB = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
            Mount = Directory.CreateDirectory(Path.Combine(DriveA, "mount")).FullName;
            File.WriteAllBytes(Path.Combine(DriveA, "a.bin"), new byte[1000]);
            File.WriteAllBytes(Path.Combine(Mount, "m.bin"), new byte[700]);
            File.WriteAllBytes(Path.Combine(DriveB, "b.bin"), new byte[300]);
            Favorite = Directory.CreateDirectory(Path.Combine(_root, "fav")).FullName;
            File.WriteAllBytes(Path.Combine(Favorite, "f.bin"), new byte[40]);
            Sources = new Sources(DriveA, DriveB, Mount, Favorite);
        }

        public string DriveA { get; }

        public string DriveB { get; }

        public string Mount { get; }

        public string Favorite { get; }

        public Sources Sources { get; }

        public ComputerPanels Panels(string? cursorOnDrive)
        {
            var provider = new ComputerFileSystemProvider(Sources, Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En));
            var entries = provider.ListAsync(ComputerAddress.Text, false, default)
                .ToBlockingEnumerable()
                .Select(result => result.Value!)
                .ToList();
            var cursor = cursorOnDrive is null ? -1 : entries.FindIndex(entry => entry.Path == cursorOnDrive);
            return new ComputerPanels(entries, cursor, ComputerAddress.Text);
        }

        public ShellHost Host(ComputerPanels panels, IServerShares? servers = null, IDialogService? dialogs = null) =>
            ShellHost.Open(Language.En, services =>
            {
                services.AddSingleton<IPanelWorkspace>(panels);
                services.AddSingleton<IComputerSources>(Sources);
                if (servers is not null)
                {
                    services.AddSingleton(servers);
                }

                if (dialogs is not null)
                {
                    services.AddSingleton(dialogs);
                }
            });

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class Sources(string a, string b, string mount, string favorite) : IComputerSources
    {
        public System.Collections.Concurrent.ConcurrentBag<int> Threads { get; } = [];

        public IReadOnlyList<DriveEntry> MountedDrives()
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            return [new DriveEntry(a, "A", "ntfs", 1, 2), new DriveEntry(b, "B", "ntfs", 1, 2)];
        }

        public IReadOnlyList<ConnectedShare> ConnectedShares() => [];

        public IReadOnlyList<UserPlace> Places() => [];

        public IReadOnlyList<HotlistItem> Favorites() => [new HotlistItem("fav", favorite)];

        public IReadOnlyList<string> MountPoints()
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            return [mount];
        }
    }

    private sealed class FailingServers : IServerShares
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Asked { get; } = [];

        public FileResult<IReadOnlyList<string>> Shares(string server)
        {
            Asked.Enqueue(server);
            return FileResult<IReadOnlyList<string>>.Fail(OperationError.Unsupported("offline", UncPath.ServerRoot(server)));
        }
    }

    private sealed class ComputerPanels(List<Entry> entries, int cursor, string path) : IPanelWorkspace
    {
        public List<string> Went { get; } = [];

        public List<string> Revealed { get; } = [];

        public PanelSide ActiveSide => PanelSide.Left;

        public PanelSnapshot Read(PanelSide side) =>
            PanelSnapshot.Empty with { Path = path, Entries = entries, Cursor = cursor };

        public PanelCapture? Capture() => null;

        public Task<bool> GoAsync(PanelSide side, string target, CancellationToken cancellationToken = default)
        {
            Went.Add(target);
            return Task.FromResult(true);
        }

        public Task<bool> RevealAsync(string target, CancellationToken cancellationToken = default)
        {
            Revealed.Add(target);
            return Task.FromResult(true);
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
