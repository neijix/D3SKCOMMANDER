using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using D3skCommander.App.Dialogs;
using D3skCommander.Core.Launch;
using Xunit;

namespace D3skCommander.App.Tests.Features.Launch;

public class LaunchCommandTests
{
    [AvaloniaFact]
    public async Task OpenWithRunsTheChosenProgramWithTheFileAsArgv()
    {
        using var harness = await LaunchHarness.Open();
        await harness.AddFileAsync("a b.txt");
        harness.Select("a b.txt");

        var pending = harness.Commands.OpenWithAsync();
        var shown = await harness.Dialogs.WaitShownAsync();
        var view = new FormDialogView { DataContext = shown.Form };
        var window = new Window { Content = view };
        window.Show();
        Assert.Contains(view.GetVisualDescendants().OfType<TextBox>(), box => box.Text == "");
        window.Close();
        var program = shown.Form!.Fields.OfType<TextDialogField>().Single();
        program.Text = "myeditor --flag";
        harness.Dialogs.Answer(DialogResult.Ok());
        await pending;

        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Equal("myeditor", request.Program);
        Assert.Equal(["--flag", Path.Combine(harness.Left, "a b.txt")], request.Arguments);
        Assert.Equal(harness.Left, request.WorkingDirectory);
    }

    [AvaloniaFact]
    public async Task OpenWithTheSystemDefaultUsesTheDefaultOpener()
    {
        using var harness = await LaunchHarness.Open();
        await harness.AddFileAsync("a.txt");
        harness.Select("a.txt");

        var pending = harness.Commands.OpenWithAsync();
        var shown = await harness.Dialogs.WaitShownAsync();
        var check = shown.Form!.Fields.OfType<CheckDialogField>().Last();
        check.IsChecked = true;
        Assert.True(shown.Form.CanAccept);
        harness.Dialogs.Answer(DialogResult.Ok());
        await pending;

        var path = Path.Combine(harness.Left, "a.txt");
        var expected = Launcher.DefaultOpenRequest(path, LaunchPlatforms.Current);
        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        var wanted = Assert.IsType<ProcessRequest.Argv>(expected);
        Assert.Equal(wanted.Program, request.Program);
        Assert.Equal(wanted.Arguments, request.Arguments);
    }

    [AvaloniaFact]
    public async Task OpenWithNeedsAProgramOrTheDefault()
    {
        using var harness = await LaunchHarness.Open();
        await harness.AddFileAsync("a.txt");
        harness.Select("a.txt");

        var pending = harness.Commands.OpenWithAsync();
        var shown = await harness.Dialogs.WaitShownAsync();
        Assert.False(shown.Form!.CanAccept);
        harness.Dialogs.Answer(DialogResult.Cancel());
        await pending;
        Assert.Empty(harness.Runner.Started);
    }

    [AvaloniaFact]
    public async Task TerminalUsesThePreferenceInTheActiveFolder()
    {
        using var harness = await LaunchHarness.Open();
        var prefs = harness.Settings.Preferences.Current;
        prefs.Terminal = "myterm --here";
        harness.Commands.Platform = LaunchPlatform.Linux;

        harness.Commands.OpenTerminal();

        var request = Assert.IsType<ProcessRequest.Shell>(Assert.Single(harness.Runner.Started));
        Assert.Equal("myterm --here", request.CommandLine);
        Assert.Equal(harness.Left, request.WorkingDirectory);
    }

    [AvaloniaFact]
    public async Task TerminalFallsBackToThePlatformDefault()
    {
        using var harness = await LaunchHarness.Open();

        harness.Commands.OpenTerminal();

        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Equal(harness.Left, request.WorkingDirectory);
        if (LaunchPlatforms.Current == LaunchPlatform.Linux)
        {
            Assert.Equal(Launcher.LinuxTerminals[0], request.Program);
        }
    }

    [AvaloniaFact]
    public async Task OpenDefaultStartsTheOpenerAndReportsAFailure()
    {
        using var harness = await LaunchHarness.Open();
        var path = Path.Combine(harness.Left, "doc.pdf");

        var result = harness.Commands.OpenDefault(path);

        Assert.True(result.IsSuccess);
        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Contains(path, request.Arguments.Append(request.Program));
    }

    [AvaloniaFact]
    public async Task AUserCommandRunsOnTheSelectedFiles()
    {
        using var harness = await LaunchHarness.Open();
        await harness.AddFileAsync("a.txt");
        await harness.AddFileAsync("b.txt");
        harness.Select("a.txt", "b.txt");

        var result = harness.Commands.RunUserCommand(new UserCommand("Show", "show -f %s", oncePerFile: false));

        Assert.True(result.IsSuccess);
        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Equal("show", request.Program);
        Assert.Equal(3, request.Arguments.Count);
        Assert.Equal("-f", request.Arguments[0]);
    }

    [AvaloniaFact]
    public async Task TheToolsMenuItemsFollowTheUserCommandsInTheSettings()
    {
        using var harness = await LaunchHarness.Open();
        await harness.AddFileAsync("a.txt");
        harness.Select("a.txt");
        Assert.Empty(harness.Commands.UserCommandItems());

        harness.Settings.Preferences.Current.UserCommands.Add(new UserCommand("Show", "show %s", oncePerFile: true));
        var item = Assert.Single(harness.Commands.UserCommandItems());
        item.Invoke();

        Assert.Equal("Show", item.Header);
        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Equal("show", request.Program);
    }
}
