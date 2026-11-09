using System.ComponentModel;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Panels;
using Xunit;

namespace D3skCommander.App.Tests.Features.Launch;

public class TerminalCommandTests
{
    [AvaloniaFact]
    public async Task TerminalHereStartsWindowsTerminalInTheTargetsFolder()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.IsOnPath = program => program == "wt";
        var sub = Path.Combine(harness.Left, "sub");
        var file = Path.Combine(sub, "a.txt");

        Task pending;
        using (harness.Workspace.UseContextTarget(Target(ContextTargetKind.File, harness.Left, file)))
        {
            pending = harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: false);
        }

        await pending;
        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Equal("wt", request.Program);
        Assert.Equal(["-d", sub], request.Arguments);
        Assert.Empty(harness.Status);
    }

    [AvaloniaFact]
    public async Task WithoutAContextTargetTheActiveFolderIsUsed()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.IsOnPath = _ => false;

        await harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: false);

        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Equal("cmd", request.Program);
        Assert.Equal(harness.Left, request.WorkingDirectory);
    }

    [AvaloniaFact]
    public async Task PowerShellHereUsesPwshWhenOnPath()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.IsOnPath = program => program == "pwsh";

        await harness.Commands.OpenTerminalHereAsync(TerminalKind.PowerShell, elevated: false);

        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Equal("pwsh", request.Program);
        Assert.Contains("-WorkingDirectory", request.Arguments);
    }

    [AvaloniaFact]
    public async Task AnElevatedStartThatTheUserDeclinesShowsAStatusTextAndNoDialog()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.IsOnPath = _ => false;
        harness.Runner.FailWith = new Win32Exception(1223);

        await harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: true);

        Assert.Equal(harness.Text.Get("launch.cancelled"), Assert.Single(harness.Status));
        Assert.Equal(0, harness.Dialogs.ShownCount);
    }

    [AvaloniaFact]
    public async Task AnElevatedTerminalInAFolderWithAPercentIsRefusedWithAStatusText()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.IsOnPath = _ => false;
        var odd = Path.Combine(harness.Left, "50%");
        Directory.CreateDirectory(odd);

        Task pending;
        using (harness.Workspace.UseContextTarget(Target(ContextTargetKind.Folder, harness.Left, odd)))
        {
            pending = harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: true);
        }

        await pending;
        var text = Assert.Single(harness.Status);
        Assert.StartsWith(harness.Text.Get("launch.refused"), text, StringComparison.Ordinal);
        Assert.Empty(harness.Runner.Started);
        Assert.Equal(0, harness.Dialogs.ShownCount);
    }

    [AvaloniaTheory]
    [InlineData(LaunchPlatform.Linux)]
    [InlineData(LaunchPlatform.MacOS)]
    public async Task AdminCommandsOffWindowsSayWindowsOnly(LaunchPlatform platform)
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = platform;

        await harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: true);
        await harness.Commands.OpenTerminalHereAsync(TerminalKind.PowerShell, elevated: true);

        Assert.Equal([harness.Text.Get("launch.windows_only"), harness.Text.Get("launch.windows_only")], harness.Status);
        Assert.Empty(harness.Runner.Started);
    }

    [AvaloniaFact]
    public async Task PowerShellOnMacOsSaysNotAvailable()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.MacOS;

        await harness.Commands.OpenTerminalHereAsync(TerminalKind.PowerShell, elevated: false);

        Assert.Equal(harness.Text.Get("launch.powershell_unavailable"), Assert.Single(harness.Status));
        Assert.Empty(harness.Runner.Started);
    }

    [AvaloniaFact]
    public async Task AStartFailureShowsTheLaunchFailedDialog()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.IsOnPath = _ => true;
        harness.Runner.FailWith = new FileNotFoundException("nope");

        await harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: false);

        var shown = await harness.Dialogs.WaitShownAsync();
        Assert.Equal(harness.Text.Get("launch.failed"), shown.Title);
        Assert.Empty(harness.Status);
        harness.Dialogs.Answer(DialogResult.Ok());
    }

    private static PanelContextTarget Target(ContextTargetKind kind, string panelFolder, string entry) =>
        new(kind, PanelProviderKind.Local, panelFolder, entry, []);
}
