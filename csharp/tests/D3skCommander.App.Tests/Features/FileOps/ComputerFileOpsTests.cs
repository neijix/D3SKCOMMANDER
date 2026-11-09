using Avalonia.Headless.XUnit;
using D3skCommander.App.Features.Computer;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static D3skCommander.App.Tests.Features.FileOps.FileOpsCommandTests;

namespace D3skCommander.App.Tests.Features.FileOps;

public class ComputerFileOpsTests
{
    private static readonly string ReadOnly = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En)
        .Get("computer.readonly");

    [AvaloniaFact]
    public async Task CopyAndMoveOntoComputerAreRefusedBeforeAnyJobAndTheFakeDriveIsUntouched()
    {
        using var rig = await Rig.Open();
        var dialogs = new ScriptedDialogs();
        var commands = Commands(rig.Panels, dialogs);
        await rig.Panels.GoAsync(PanelSide.Right, ComputerAddress.Text, TestContext.Current.CancellationToken);
        var listed = rig.Panels.Read(PanelSide.Left);
        rig.Panels.ApplySelection(PanelSide.Left, At(listed, "a.txt"), []);

        await commands.CopyAsync();
        await commands.MoveAsync();

        Assert.Equal([ReadOnly, ReadOnly], dialogs.Messages);
        Assert.True(File.Exists(Path.Combine(rig.Mount, "a.txt")));
        Assert.Equal(["a.txt"], Names(rig.Mount));
    }

    [AvaloniaFact]
    public async Task DeleteMoveCutRenameAndNewFolderAtComputerAreRefusedAndDriveFoldersStay()
    {
        using var rig = await Rig.Open();
        var dialogs = new ScriptedDialogs { Text = "made", Confirm = true };
        var commands = Commands(rig.Panels, dialogs);
        await rig.Panels.GoAsync(PanelSide.Left, ComputerAddress.Text, TestContext.Current.CancellationToken);
        var listed = rig.Panels.Read(PanelSide.Left);
        rig.Panels.ApplySelection(PanelSide.Left, listed.Entries.ToList().FindIndex(entry => entry.Path == rig.Mount), []);

        await commands.DeleteAsync();
        await commands.DeleteAsync(D3skCommander.Core.Commands.CommandId.DeletePermanent);
        await commands.MoveAsync();
        await commands.ClipAsync(cut: true);
        await commands.RenameAsync(rig.Mount, "renamed");
        await commands.NewFolderAsync();

        Assert.Equal(6, dialogs.Messages.Count);
        Assert.All(dialogs.Messages, message => Assert.Equal(ReadOnly, message));
        Assert.Empty(dialogs.Confirmed);
        Assert.True(Directory.Exists(rig.Mount));
        Assert.Equal(["a.txt"], Names(rig.Mount));
        Assert.True(commands.Clipboard.IsEmpty);
    }

    [AvaloniaFact]
    public async Task PasteAtComputerIsRefusedAndCopyToClipboardStillWorks()
    {
        using var rig = await Rig.Open();
        var dialogs = new ScriptedDialogs();
        var commands = Commands(rig.Panels, dialogs);
        var listed = rig.Panels.Read(PanelSide.Left);
        rig.Panels.ApplySelection(PanelSide.Left, At(listed, "a.txt"), []);
        await commands.ClipAsync(cut: false);
        await rig.Panels.GoAsync(PanelSide.Left, ComputerAddress.Text, TestContext.Current.CancellationToken);

        await commands.PasteAsync();

        Assert.False(commands.Clipboard.IsEmpty);
        Assert.Equal([ReadOnly], dialogs.Messages);
        Assert.Equal(["a.txt"], Names(rig.Mount));
    }

    [AvaloniaFact]
    public async Task ATargetTypedAsTheAddressInTheCopyDialogIsRefused()
    {
        using var rig = await Rig.Open();
        var dialogs = new ScriptedDialogs();
        var commands = Commands(rig.Panels, dialogs);
        var listed = rig.Panels.Read(PanelSide.Left);
        rig.Panels.ApplySelection(PanelSide.Left, At(listed, "a.txt"), []);
        await rig.Panels.GoAsync(PanelSide.Right, ComputerAddress.Text, TestContext.Current.CancellationToken);

        await commands.CopyAsync();

        Assert.NotNull(dialogs.Form);
        Assert.Equal([ReadOnly], dialogs.Messages);
        Assert.Equal(["a.txt"], Names(rig.Mount));
    }

    [AvaloniaFact]
    public async Task CopyFromComputerRowsIsNotRefused()
    {
        using var rig = await Rig.Open();
        var dialogs = new ScriptedDialogs { Choice = 0 };
        var commands = Commands(rig.Panels, dialogs);
        await rig.Panels.GoAsync(PanelSide.Left, ComputerAddress.Text, TestContext.Current.CancellationToken);
        var listed = rig.Panels.Read(PanelSide.Left);
        rig.Panels.ApplySelection(PanelSide.Left, listed.Entries.ToList().FindIndex(entry => entry.Path == rig.Mount), []);

        await commands.CopyAsync();

        Assert.DoesNotContain(ReadOnly, dialogs.Messages);
        Assert.NotNull(dialogs.Form);
    }

    private static string[] Names(string folder) =>
        [.. Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Order()!];

    private sealed class Rig : IDisposable
    {
        private readonly string _root;
        private readonly ServiceProvider _services;

        private Rig(string root, ServiceProvider services, string mount)
        {
            _root = root;
            _services = services;
            Mount = mount;
            Panels = services.GetRequiredService<PanelWorkspace>();
        }

        public string Mount { get; }

        public PanelWorkspace Panels { get; }

        public static async Task<Rig> Open()
        {
            var root = Directory.CreateTempSubdirectory("d3sk-computer-ops-").FullName;
            var mount = Directory.CreateDirectory(Path.Combine(root, "usb")).FullName;
            File.WriteAllText(Path.Combine(mount, "a.txt"), "x");
            var services = TestComposition.Build(configure: s =>
            {
                s.AddSingleton<IComputerSources>(new Sources(mount));
                s.AddSingleton(new ComputerOptions([root]));
            });
            var rig = new Rig(root, services, mount);
            await rig.Panels.OpenAsync(mount, mount, TestContext.Current.CancellationToken);
            return rig;
        }

        public void Dispose()
        {
            Panels.Dispose();
            _services.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
    }

    private sealed class Sources(string mount) : IComputerSources
    {
        public IReadOnlyList<DriveEntry> MountedDrives() => [new DriveEntry(mount, "USB", "ntfs", 1, 2)];

        public IReadOnlyList<ConnectedShare> ConnectedShares() => [];

        public IReadOnlyList<UserPlace> Places() => [];

        public IReadOnlyList<HotlistItem> Favorites() => [];

        public IReadOnlyList<string> MountPoints() => [mount];
    }
}
