using Avalonia.Headless.XUnit;
using D3skCommander.App.Features.Launch;
using D3skCommander.App.Shell;
using D3skCommander.Core.Launch;
using Xunit;

namespace D3skCommander.App.Tests.Features.Launch;

public class CommandLineTests
{
    [AvaloniaFact]
    public async Task APlainLineRunsInTheActiveFolder()
    {
        using var harness = await LaunchHarness.Open();
        var line = harness.NewLine();
        line.Line = "echo hi";

        await line.RunAsync();

        var request = Assert.IsType<ProcessRequest.Shell>(Assert.Single(harness.Runner.Started));
        Assert.Equal("echo hi", request.CommandLine);
        Assert.Equal(harness.Left, request.WorkingDirectory);
        Assert.Equal("", line.Line);
    }

    [AvaloniaFact]
    public async Task CdMovesTheActivePanelAndStartsNothing()
    {
        using var harness = await LaunchHarness.Open();
        var line = harness.NewLine();
        line.Line = "cd sub";

        await line.RunAsync();

        Assert.Empty(harness.Runner.Started);
        Assert.Equal(Path.Combine(harness.Left, "sub"), harness.Workspace.Read(PanelSide.Left).Path);
        Assert.Equal(Path.Combine(harness.Left, "sub") + ">", line.Prompt);
    }

    [AvaloniaFact]
    public async Task CdToAMissingFolderShowsAnErrorAndStaysPut()
    {
        using var harness = await LaunchHarness.Open();
        var line = harness.NewLine();
        line.Line = "cd nowhere";

        var pending = line.RunAsync();
        await harness.Dialogs.WaitShownAsync();
        harness.Dialogs.Answer(D3skCommander.App.Dialogs.DialogResult.Ok());
        await pending;

        Assert.Empty(harness.Runner.Started);
        Assert.Equal(harness.Left, harness.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task AnEmptyLineDoesNothing()
    {
        using var harness = await LaunchHarness.Open();
        var line = harness.NewLine();
        line.Line = "   ";

        await line.RunAsync();

        Assert.Empty(harness.Runner.Started);
    }

    [AvaloniaFact]
    public async Task ThePromptNamesTheActiveFolderAndTheHintIsLocalized()
    {
        using var harness = await LaunchHarness.Open();
        var line = harness.NewLine();

        line.Refresh();
        Assert.Equal(harness.Left + ">", line.Prompt);
        harness.Workspace.Activate(PanelSide.Right);
        line.Refresh();
        Assert.Equal(harness.Right + ">", line.Prompt);
        Assert.Equal(harness.Text.Get("launch.hint"), line.Hint);
        Assert.NotEqual("launch.hint", line.Hint);
    }
}
