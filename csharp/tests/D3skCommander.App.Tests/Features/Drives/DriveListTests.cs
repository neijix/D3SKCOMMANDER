using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Panels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Drives;

public class DriveListTests
{
    [AvaloniaFact]
    public async Task TheMenuShowsDrivesWithFreeSpaceAndConnectedShares()
    {
        using var harness = await DrivesHarness.Open();
        await harness.Commands.ChooseDriveAsync(PanelSide.Left);

        var texts = harness.Menu.Shown.Select(item => item.Text).ToList();
        Assert.Contains(DrivesHarness.DriveRoot + " - 1.0 KB free", texts);
        Assert.Contains("smb://nas/media (network)", texts);
        var volume = harness.Menu.Shown.First(item => item.Kind == DriveMenuKind.Volume);
        Assert.Equal(0.75, volume.UsedFraction);
        Assert.True(volume.IsCurrent);
        Assert.Equal(harness.Left, harness.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task TheComputerLineComesFirstAndOpensComputerOnTheMenusSide()
    {
        using var harness = await DrivesHarness.Open();
        harness.Menu.Pick = DriveMenuKind.Computer;
        await harness.Commands.ChooseDriveAsync(PanelSide.Right);

        var first = harness.Menu.Shown[0];
        Assert.Equal(DriveMenuKind.Computer, first.Kind);
        Assert.Equal("Home", first.Text);
        Assert.False(first.IsCurrent);
        Assert.DoesNotContain(harness.Menu.Shown.Skip(1), item => item.Section == first.Section);
        Assert.Equal(harness.Left, harness.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task TheComputerLineOpensTheRealComputerView()
    {
        var menu = new FakeMenu { Pick = DriveMenuKind.Computer };
        using var services = TestComposition.Build(configure: s => s.AddSingleton<IDriveMenuPresenter>(menu));
        var workspace = services.GetRequiredService<PanelWorkspace>();
        var home = Path.GetTempPath();
        await workspace.OpenAsync(home, home, TestContext.Current.CancellationToken);
        try
        {
            var commands = services.GetRequiredService<DrivesCommands>();
            await commands.ChooseDriveAsync(PanelSide.Right);

            Assert.Equal(ComputerAddress.Text, workspace.Read(PanelSide.Right).Path);
            Assert.Equal(home, workspace.Read(PanelSide.Left).Path);
            await commands.ChooseDriveAsync(PanelSide.Right);
            Assert.True(menu.Shown[0].IsCurrent);
            Assert.DoesNotContain(menu.Shown.Skip(1), item => item.IsCurrent);
        }
        finally
        {
            workspace.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task AChosenDriveOpensInTheRightPanel()
    {
        using var harness = await DrivesHarness.Open();
        harness.Menu.Pick = DriveMenuKind.Volume;
        await harness.Commands.ChooseDriveAsync(PanelSide.Right);

        Assert.Equal(Path.GetPathRoot(harness.Right), harness.Workspace.Read(PanelSide.Right).Path);
        Assert.Equal(harness.Left, harness.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task AnUnmountedDiskIsMountedThenOpened()
    {
        // udisksctl prints a Unix mount path ("Mounted /dev/sdb1 at /media/..."): the fake's
        // temp folder is one only on Linux and macOS.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Mounting goes through udisksctl, which prints Unix paths");
        using var harness = await DrivesHarness.Open();
        harness.Devices.Json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "lsblk", "2.41.3", "with-usb.json"));
        harness.Menu.Pick = DriveMenuKind.Unmounted;
        await harness.Commands.ChooseDriveAsync(PanelSide.Left);

        Assert.Contains(harness.Menu.Shown, item => item.Kind == DriveMenuKind.Unmounted && item.Text == "Mount KEY32 (29.7 GB)");
        Assert.Equal(["mount /dev/sdb1"], harness.Devices.Calls);
        Assert.Equal(harness.Media, harness.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task EjectUnmountsThenPowersOffAndLeavesTheVolume()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Eject unmounts block devices with udisks: Linux only");
        using var harness = await DrivesHarness.Open();
        var mount = DrivesHarness.DriveRoot;
        harness.Devices.Json = EjectableAt(mount);
        harness.Menu.Pick = DriveMenuKind.Eject;
        await harness.Commands.ChooseDriveAsync(PanelSide.Left);

        Assert.Equal(["unmount /dev/sdb1", "poweroff /dev/sdb"], harness.Devices.Calls);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), harness.Workspace.Read(PanelSide.Left).Path);
    }

    private static string EjectableAt(string mount) =>
        "{\"blockdevices\":[{\"name\":\"sdb\",\"path\":\"/dev/sdb\",\"size\":100,\"fstype\":null,\"label\":null,\"mountpoints\":[null],\"rm\":true,\"hotplug\":true,\"type\":\"disk\","
        + "\"children\":[{\"name\":\"sdb1\",\"path\":\"/dev/sdb1\",\"size\":90,\"fstype\":\"ext4\",\"label\":null,\"mountpoints\":[\"" + mount + "\"],\"rm\":true,\"hotplug\":true,\"type\":\"part\"}]}]}";

    [AvaloniaFact]
    public async Task TheKeyCommandsAreHandled()
    {
        using var harness = await DrivesHarness.Open();
        Assert.True(harness.Shell.CanExecute(CommandId.DrivesLeft));
        Assert.True(harness.Shell.CanExecute(CommandId.DrivesRight));
        Assert.True(harness.Shell.CanExecute(CommandId.Properties));
    }

    [AvaloniaFact]
    public async Task PropertiesOfAFileListsItsFacts()
    {
        using var harness = await DrivesHarness.Open();
        File.WriteAllText(Path.Combine(harness.Left, "note.txt"), "12345");
        await harness.Workspace.RefreshAsync(TestContext.Current.CancellationToken);
        harness.Workspace.ApplySelection(PanelSide.Left, Cursor(harness, "note.txt"), []);
        var pending = harness.Commands.PropertiesAsync();
        var shown = await harness.Dialogs.WaitShownAsync();
        var lines = shown.Form!.Fields.Select(field => field.Label).ToList();
        Assert.Contains("Name: note.txt", lines);
        Assert.Contains("Type: File", lines);
        Assert.Contains(lines, line => line.StartsWith("Size: 5 B (5 bytes)", StringComparison.Ordinal));
        harness.Dialogs.Answer(DialogResult.Ok());
        await pending;
    }

    [AvaloniaFact]
    public async Task PropertiesOfAFolderMeasuresItsTree()
    {
        using var harness = await DrivesHarness.Open();
        var folder = Path.Combine(harness.Left, "box");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a"), "123");
        await harness.Workspace.RefreshAsync(TestContext.Current.CancellationToken);
        harness.Workspace.ApplySelection(PanelSide.Left, Cursor(harness, "box"), []);
        var pending = harness.Commands.PropertiesAsync();
        var shown = await harness.Dialogs.WaitShownAsync();
        var lines = shown.Form!.Fields.Select(field => field.Label).ToList();
        Assert.Contains("Type: Folder", lines);
        Assert.Contains("Contents: 1 files, 0 folders", lines);
        harness.Dialogs.Answer(DialogResult.Ok());
        await pending;
    }

    private static int Cursor(DrivesHarness harness, string name)
    {
        var snap = harness.Workspace.Read(PanelSide.Left);
        for (var i = 0; i < snap.Entries.Count; i++)
        {
            if (snap.Entries[i].Name == name)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Missing " + name);
    }
}
