using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using Xunit;

namespace D3skCommander.App.Tests.Features.Drives;

public class DriveListTests
{
    [AvaloniaFact]
    public async Task TheListShowsDrivesWithFreeSpaceAndConnectedShares()
    {
        using var harness = await DrivesHarness.Open();
        var pending = harness.Commands.ChooseDriveAsync(PanelSide.Left);
        var shown = await harness.Dialogs.WaitShownAsync();
        Assert.Equal([DrivesHarness.DriveRoot + " - 1.0 KB free", "smb://nas/media (network)"], shown.Choices);
        harness.Dialogs.Answer(DialogResult.Cancel());
        await pending;

        Assert.Equal(harness.Left, harness.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task AChosenDriveOpensInTheRightPanel()
    {
        using var harness = await DrivesHarness.Open();
        harness.Dialogs.Enqueue(DialogResult.Picked(0));
        await harness.Commands.ChooseDriveAsync(PanelSide.Right);

        Assert.Equal(Path.GetPathRoot(harness.Right), harness.Workspace.Read(PanelSide.Right).Path);
        Assert.Equal(harness.Left, harness.Workspace.Read(PanelSide.Left).Path);
    }

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
