using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
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
