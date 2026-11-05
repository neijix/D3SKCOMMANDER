using Avalonia.Headless.XUnit;
using D3skCommander.App.Shell;
using Xunit;

namespace D3skCommander.App.Tests.Features.Drives;

public class OsDropTests
{
    [AvaloniaFact]
    public async Task DroppedFilesAreCopiedIntoThePanelFolder()
    {
        using var harness = await DrivesHarness.Open();
        var outside = Path.Combine(harness.Media, "photo.jpg");
        File.WriteAllText(outside, "pixels");
        var folder = Path.Combine(harness.Media, "album");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "song.mp3"), "notes");

        await harness.Commands.DropAsync(PanelSide.Right, [outside, folder, Path.Combine(harness.Media, "ghost")]);

        Assert.Equal("pixels", File.ReadAllText(Path.Combine(harness.Right, "photo.jpg")));
        Assert.Equal("notes", File.ReadAllText(Path.Combine(harness.Right, "album", "song.mp3")));
        Assert.True(File.Exists(outside));
        Assert.Empty(Directory.GetFileSystemEntries(harness.Left));
    }

    [AvaloniaFact]
    public async Task TheWorkspaceHookRunsTheCopy()
    {
        using var harness = await DrivesHarness.Open();
        var outside = Path.Combine(harness.Media, "a.txt");
        File.WriteAllText(outside, "x");
        harness.Workspace.DropFiles(PanelSide.Left, [outside]);

        var target = Path.Combine(harness.Left, "a.txt");
        for (var i = 0; i < 100 && !File.Exists(target); i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(target));
    }
}
