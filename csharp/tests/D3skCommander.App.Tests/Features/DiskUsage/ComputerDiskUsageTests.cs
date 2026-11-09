using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.DiskUsage;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Shell;
using D3skCommander.Core.Commands;
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
        await model.ScanTask.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(ComputerAddress.Text, model.RootPath);
        Assert.Equal(host.Localizer.Get("computer.title"), model.Root!.Name);
        Assert.Equal(ComputerAddress.Text, model.Root.Path);
        Assert.Equal(2, model.Rows.Count);
        // Drive A holds 1000 bytes of its own and a 700 byte mount point that is skipped.
        Assert.Equal(1000 + 300, model.Root.Bytes);
    }

    [AvaloniaFact]
    public async Task ADriveRowScansThatFolderAsToday()
    {
        using var rig = new Rig();
        using var host = rig.Host(rig.Panels(cursorOnDrive: rig.DriveB));

        host.Window.Dispatch(CommandId.DiskUsage);
        var model = host.Provider.GetRequiredService<DiskUsageViewModel>();
        await model.ScanTask.WaitAsync(Wait, TestContext.Current.CancellationToken);

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
        await model.ScanTask.WaitAsync(Wait, TestContext.Current.CancellationToken);

        File.WriteAllBytes(Path.Combine(rig.DriveB, "more.bin"), new byte[50]);
        await model.RescanAsync().WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal(ComputerAddress.Text, model.RootPath);
        Assert.Equal(1000 + 350, model.Root!.Bytes);

        model.Selected = null;
        Assert.True(await model.ShowInPanelAsync());
        Assert.Equal([ComputerAddress.Text], panels.Went);
        Assert.Empty(panels.Revealed);
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
            Sources = new Sources(DriveA, DriveB, Mount);
        }

        public string DriveA { get; }

        public string DriveB { get; }

        public string Mount { get; }

        public Sources Sources { get; }

        public ComputerPanels Panels(string? cursorOnDrive)
        {
            var provider = new ComputerFileSystemProvider(Sources, Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En));
            var entries = provider.ListAsync(ComputerAddress.Text, false, default)
                .ToBlockingEnumerable()
                .Select(result => result.Value!)
                .ToList();
            var cursor = cursorOnDrive is null ? -1 : entries.FindIndex(entry => entry.Path == cursorOnDrive);
            return new ComputerPanels(entries, cursor);
        }

        public ShellHost Host(ComputerPanels panels) =>
            ShellHost.Open(Language.En, services =>
            {
                services.AddSingleton<IPanelWorkspace>(panels);
                services.AddSingleton<IComputerSources>(Sources);
            });

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class Sources(string a, string b, string mount) : IComputerSources
    {
        public IReadOnlyList<DriveEntry> MountedDrives() =>
            [new DriveEntry(a, "A", "ntfs", 1, 2), new DriveEntry(b, "B", "ntfs", 1, 2)];

        public IReadOnlyList<ConnectedShare> ConnectedShares() => [];

        public IReadOnlyList<UserPlace> Places() => [];

        public IReadOnlyList<HotlistItem> Favorites() => [];

        public IReadOnlyList<string> MountPoints() => [mount];
    }

    private sealed class ComputerPanels(List<Entry> entries, int cursor) : IPanelWorkspace
    {
        public List<string> Went { get; } = [];

        public List<string> Revealed { get; } = [];

        public PanelSide ActiveSide => PanelSide.Left;

        public PanelSnapshot Read(PanelSide side) =>
            PanelSnapshot.Empty with { Path = ComputerAddress.Text, Entries = entries, Cursor = cursor };

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
