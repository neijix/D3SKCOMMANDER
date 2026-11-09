using System.IO.Compression;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Archives;
using D3skCommander.App.Features.Catalog;
using D3skCommander.App.Features.Crypt;
using D3skCommander.App.Features.Computer;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.Sync;
using D3skCommander.App.Features.Tools;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Computer;

/// <summary>Tools that write into a folder refuse Computer as that folder: the read-only line, no job, no file.</summary>
public class ComputerToolTargetsTests
{
    [AvaloniaTheory]
    [InlineData("pack")]
    [InlineData("unpack")]
    [InlineData("backup")]
    [InlineData("split")]
    [InlineData("combine")]
    [InlineData("encrypt")]
    [InlineData("decrypt")]
    [InlineData("catalog")]
    public async Task ToolsWithTheTargetPanelAtComputerShowTheReadOnlyLineAndStartNoJob(string tool)
    {
        using var rig = await Rig.Open(targetAtComputer: true);

        var running = rig.Run(tool);
        var shown = await rig.Dialogs.WaitShownAsync();
        while (shown.Kind != DialogKind.Message)
        {
            rig.Fill(shown);
            rig.Dialogs.Answer(shown.Kind == DialogKind.Text ? DialogResult.Ok(shown.InitialText) : DialogResult.Ok());
            shown = await rig.Dialogs.WaitShownAsync();
        }

        rig.Dialogs.Answer(DialogResult.Ok());
        await running.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Equal(rig.Text.Get("computer.readonly"), shown.Text);
        rig.AssertNothingStarted();
    }

    [AvaloniaTheory]
    [InlineData("checksum")]
    [InlineData("catalog")]
    public async Task ChecksumCreateAndCatalogWithTheActivePanelAtComputerShowTheReadOnlyLineAndStartNoJob(string tool)
    {
        using var rig = await Rig.Open(targetAtComputer: false);

        var running = rig.Run(tool);
        var shown = await rig.Dialogs.WaitShownAsync();
        rig.Dialogs.Answer(DialogResult.Ok());
        await running.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Equal(DialogKind.Message, shown.Kind);
        Assert.Equal(rig.Text.Get("computer.readonly"), shown.Text);
        Assert.Equal(1, rig.Dialogs.ShownCount);
        rig.AssertNothingStarted();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SyncWithEitherSideAtComputerShowsTheReadOnlyLineAtCompareAndApply(bool left)
    {
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var jobs = new JobService();
        var work = Path.Combine(Path.GetTempPath(), "d3sk-computer-sync-never-created");
        var view = new SyncViewModel(
            left ? ComputerAddress.Text : work,
            left ? work : ComputerAddress.Text,
            jobs,
            text,
            new FakeFolderPicker(),
            null);

        view.Compare();
        Assert.Equal(text.Get("computer.readonly"), view.StatusLine);
        Assert.Null(view.ScanJob);

        view.StatusLine = "";
        view.CanRun = true;
        view.Run();
        Assert.Equal(text.Get("computer.readonly"), view.StatusLine);
        Assert.Null(view.ApplyJob);
        Assert.Empty(jobs.Jobs);
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _root;
        private readonly string _work;
        private readonly string _mount;

        private Rig(string root, string work, string mount, ServiceProvider services, FakeDialogService dialogs)
        {
            _root = root;
            _work = work;
            _mount = mount;
            Services = services;
            Dialogs = dialogs;
            Text = services.GetRequiredService<ILocalizer>();
        }

        public ServiceProvider Services { get; }

        public FakeDialogService Dialogs { get; }

        public ILocalizer Text { get; }

        public static async Task<Rig> Open(bool targetAtComputer)
        {
            var stop = TestContext.Current.CancellationToken;
            var root = Directory.CreateTempSubdirectory("d3sk-computer-tools-").FullName;
            var work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
            var mount = Directory.CreateDirectory(Path.Combine(root, "usb")).FullName;
            await File.WriteAllTextAsync(Path.Combine(work, "a.txt"), "hello", stop);
            using (var zip = ZipFile.Open(Path.Combine(work, "b.zip"), ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(Path.Combine(work, "a.txt"), "a.txt");
            }

            var dialogs = new FakeDialogService();
            var services = TestComposition.Build(configure: s =>
            {
                s.AddSingleton<IComputerSources>(new OneDrive(mount));
                s.AddSingleton(new ComputerOptions([root]));
                s.AddSingleton<IDialogService>(dialogs);
                s.AddSingleton(new CryptOptions(D3skCommander.Core.Crypt.EncryptionCost.Minimum));
            });
            var rig = new Rig(root, work, mount, services, dialogs);
            var panels = services.GetRequiredService<PanelWorkspace>();
            await panels.OpenAsync(work, mount, stop);
            await panels.GoAsync(targetAtComputer ? PanelSide.Right : PanelSide.Left, ComputerAddress.Text, stop);
            panels.Activate(PanelSide.Left);
            if (targetAtComputer)
            {
                panels.ApplySelection(PanelSide.Left, 0, [Path.Combine(work, "a.txt"), Path.Combine(work, "b.zip")]);
            }

            return rig;
        }

        public Task Run(string tool)
        {
            var archives = Services.GetRequiredService<ArchiveCommands>();
            var tools = Services.GetRequiredService<ToolsCommands>();
            var crypt = Services.GetRequiredService<CryptCommands>();
            return tool switch
            {
                "pack" => archives.PackAsync(),
                "unpack" => archives.UnpackAsync(),
                "backup" => archives.BackupAsync(),
                "split" => tools.SplitAsync(),
                "combine" => tools.CombineAsync(),
                "checksum" => tools.ChecksumCreateAsync(),
                "encrypt" => crypt.EncryptAsync(),
                "decrypt" => crypt.DecryptAsync(),
                _ => Services.GetRequiredService<CatalogCommands>().RunAsync(),
            };
        }

        /// <summary>Gives a shown form what its Ready rule needs (a password pair), so the dialog can be accepted.</summary>
        public void Fill(DialogRequest shown)
        {
            if (shown.Form is null)
            {
                return;
            }

            foreach (var field in shown.Form.Fields)
            {
                if (field is TextDialogField { IsPassword: true } secret)
                {
                    secret.Text = "secret";
                }
                else if (field is TextDialogField { Label: var label } size && label == Text.Get("dlg.part_size"))
                {
                    size.Text = "1K";
                }
            }
        }

        public void AssertNothingStarted()
        {
            Assert.Empty(Services.GetRequiredService<IJobService>().Jobs);
            var names = new List<string>();
            foreach (var path in Directory.GetFileSystemEntries(_work))
            {
                names.Add(Path.GetFileName(path));
            }

            names.Sort(StringComparer.Ordinal);
            Assert.Equal(["a.txt", "b.zip"], names);
            Assert.Empty(Directory.GetFileSystemEntries(_mount));
        }

        public void Dispose()
        {
            Services.GetRequiredService<PanelWorkspace>().Dispose();
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
