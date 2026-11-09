using System.ComponentModel;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Launch;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
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
        harness.Commands.ResolveProgram = program => program == "wt" ? Path.Combine(Path.GetTempPath(), "wt.exe") : null;
        var sub = Path.Combine(harness.Left, "sub");
        var file = Path.Combine(sub, "a.txt");

        Task pending;
        using (harness.Workspace.UseContextTarget(Target(ContextTargetKind.File, harness.Left, file)))
        {
            pending = harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: false);
        }

        await pending;
        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "wt.exe"), request.Program);
        Assert.Equal(["-d", sub], request.Arguments);
        Assert.Empty(harness.Status);
    }

    [AvaloniaFact]
    public async Task WithoutAContextTargetTheActiveFolderIsUsed()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.ResolveProgram = _ => null;

        await harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: false);

        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), request.Program);
        Assert.Equal(harness.Left, request.WorkingDirectory);
    }

    [AvaloniaFact]
    public async Task PowerShellHereUsesPwshWhenOnPath()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.ResolveProgram = program => program == "pwsh" ? Path.Combine(Path.GetTempPath(), "pwsh.exe") : null;

        await harness.Commands.OpenTerminalHereAsync(TerminalKind.PowerShell, elevated: false);

        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "pwsh.exe"), request.Program);
        Assert.Contains("-WorkingDirectory", request.Arguments);
    }

    [AvaloniaFact]
    public async Task AnElevatedStartThatTheUserDeclinesShowsAStatusTextAndNoDialog()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.ResolveProgram = _ => null;
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
        harness.Commands.ResolveProgram = _ => null;
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

    [AvaloniaFact]
    public async Task FromTheMenuBarAnArchivePanelOpensNoTerminalAndShowsAStatus()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.ResolveProgram = _ => null;
        var zip = Path.Combine(harness.Left, "pack.zip");
        using (var stream = File.Create(zip))
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
        {
            archive.CreateEntry("in.txt");
        }

        Assert.True(await harness.Workspace.GoAsync(PanelSide.Left, zip, TestContext.Current.CancellationToken));
        await harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: false);
        await harness.Commands.OpenTerminalHereAsync(TerminalKind.PowerShell, elevated: true);

        Assert.Equal([harness.Text.Get("launch.panel_unavailable"), harness.Text.Get("launch.panel_unavailable")], harness.Status);
        Assert.Empty(harness.Runner.Started);
    }

    [AvaloniaFact]
    public async Task ARefusedElevatedStartShowsTheLocalizedReason()
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.ResolveProgram = _ => null;
        var odd = Path.Combine(harness.Left, "50%");
        Directory.CreateDirectory(odd);

        Task pending;
        using (harness.Workspace.UseContextTarget(Target(ContextTargetKind.Folder, harness.Left, odd)))
        {
            pending = harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: true);
        }

        await pending;
        Assert.Equal(harness.Text.Get("launch.refused") + ": " + harness.Text.Get("launch.refused.unsafe_folder"), Assert.Single(harness.Status));
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
        harness.Commands.ResolveProgram = _ => Path.Combine(Path.GetTempPath(), "x.exe");
        harness.Runner.FailWith = new FileNotFoundException("nope");

        await harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: false);

        var shown = await harness.Dialogs.WaitShownAsync();
        Assert.Equal(harness.Text.Get("launch.failed"), shown.Title);
        Assert.Empty(harness.Status);
        harness.Dialogs.Answer(DialogResult.Ok());
    }

    [AvaloniaTheory]
    [InlineData(PanelProviderKind.Archive)]
    [InlineData(PanelProviderKind.Remote)]
    public async Task AContextTargetOfAnArchiveOrRemotePanelOpensNoTerminalAndShowsAStatus(PanelProviderKind provider)
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.ResolveProgram = _ => null;

        Task pending;
        using (harness.Workspace.UseContextTarget(new PanelContextTarget(ContextTargetKind.Folder, provider, harness.Left, harness.Left, [])))
        {
            pending = harness.Commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: false);
        }

        await pending;
        Assert.Equal(harness.Text.Get("launch.panel_unavailable"), Assert.Single(harness.Status));
        Assert.Empty(harness.Runner.Started);
    }

    [AvaloniaFact]
    public async Task ThePlainTerminalCommandShowsTheLocalizedStatusWhenNoTerminalStarts()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The terminal candidate list is the Linux default");
        using var harness = await LaunchHarness.Open();
        harness.Runner.FailWith = new FileNotFoundException("nope");

        harness.Commands.OpenTerminal();

        Assert.Equal(harness.Text.Get("launch.refused") + ": " + harness.Text.Get("launch.refused.no_terminal"), Assert.Single(harness.Status));
        Assert.Equal(0, harness.Dialogs.ShownCount);
    }

    [AvaloniaFact]
    public async Task AFourTerminalCommandWithNoTargetAtAllFailsClosedWithAStatus()
    {
        using var harness = await LaunchHarness.Open();
        var commands = new LaunchCommands(new NoTargetWorkspace(), harness.Dialogs, harness.Text, harness.Runner, harness.Settings)
        {
            Platform = LaunchPlatform.Windows,
            ResolveProgram = _ => Path.Combine(Path.GetTempPath(), "wt.exe"),
        };
        var status = new List<string>();
        commands.ShowStatus = status.Add;

        await commands.OpenTerminalHereAsync(TerminalKind.Terminal, elevated: false);

        Assert.Equal(harness.Text.Get("launch.panel_unavailable"), Assert.Single(status));
        Assert.Empty(harness.Runner.Started);
    }

    [AvaloniaTheory]
    [InlineData(PanelProviderKind.Archive)]
    [InlineData(PanelProviderKind.Remote)]
    public async Task ThePlainTerminalOnAnArchiveOrRemoteContextTargetShowsAStatusAndStartsNothing(PanelProviderKind provider)
    {
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.ResolveProgram = _ => null;

        using (harness.Workspace.UseContextTarget(new PanelContextTarget(ContextTargetKind.Folder, provider, harness.Left, harness.Left, [])))
        {
            harness.Commands.OpenTerminal();
        }

        Assert.Equal(harness.Text.Get("launch.panel_unavailable"), Assert.Single(harness.Status));
        Assert.Empty(harness.Runner.Started);
    }

    [AvaloniaFact]
    public async Task ThePlainTerminalWithNoTargetAtAllFailsClosedWithAStatus()
    {
        using var harness = await LaunchHarness.Open();
        var commands = new LaunchCommands(new NoTargetWorkspace(), harness.Dialogs, harness.Text, harness.Runner, harness.Settings);
        var status = new List<string>();
        commands.ShowStatus = status.Add;

        commands.OpenTerminal();

        Assert.Equal(harness.Text.Get("launch.panel_unavailable"), Assert.Single(status));
        Assert.Empty(harness.Runner.Started);
    }

    [AvaloniaFact]
    public async Task ThePlainTerminalOnWindowsStartsTheSystemCmdByFullPath()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The system cmd.exe path is a Windows one");
        using var harness = await LaunchHarness.Open();
        harness.Commands.Platform = LaunchPlatform.Windows;

        harness.Commands.OpenTerminal();

        var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(harness.Runner.Started));
        Assert.True(Path.IsPathFullyQualified(request.Program));
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), request.Program);
        Assert.Equal(["/c", "start", request.Program], request.Arguments);
        Assert.Equal(harness.Left, request.WorkingDirectory);
    }

    [AvaloniaFact]
    public async Task AWindowsOverrideProgramComesFromPathNeverFromTheTargetFolder()
    {
        using var harness = await LaunchHarness.Open();
        var tools = Directory.CreateDirectory(Path.Combine(harness.Root, "tools")).FullName;
        await File.WriteAllTextAsync(Path.Combine(tools, "wt.exe"), "x", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(harness.Left, "wt.cmd"), "x", TestContext.Current.CancellationToken);
        harness.Settings.Preferences.Current.Terminal = "wt -d .";
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.ResolveProgram = program => ExecutablePath.Resolve(program, tools + Path.PathSeparator + harness.Left, ".cmd;.exe", windows: true);

        harness.Commands.OpenTerminal();

        var request = Assert.IsType<ProcessRequest.Shell>(Assert.Single(harness.Runner.Started));
        Assert.Equal("\"\"" + Path.Combine(tools, "wt.exe") + "\" -d .\"", request.CommandLine);
        Assert.DoesNotContain("wt.cmd", request.CommandLine, StringComparison.Ordinal);
        Assert.Empty(harness.Status);
    }

    [AvaloniaFact]
    public async Task AWindowsOverrideProgramThatDoesNotResolveShowsAStatusAndStartsNothing()
    {
        using var harness = await LaunchHarness.Open();
        harness.Settings.Preferences.Current.Terminal = "nosuchterm --x";
        harness.Commands.Platform = LaunchPlatform.Windows;
        harness.Commands.ResolveProgram = _ => null;

        harness.Commands.OpenTerminal();

        Assert.Equal(harness.Text.Get("launch.refused") + ": " + harness.Text.Get("launch.refused.program_missing"), Assert.Single(harness.Status));
        Assert.Empty(harness.Runner.Started);
        Assert.Equal(0, harness.Dialogs.ShownCount);
    }

    private sealed class NoTargetWorkspace : IPanelWorkspace
    {
        public PanelSide ActiveSide => PanelSide.Left;

        public PanelSnapshot Read(PanelSide side) => throw new NotSupportedException();

        public PanelCapture? Capture() => throw new NotSupportedException("A refused command never reads the capture");

        public Task<bool> GoAsync(PanelSide side, string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static PanelContextTarget Target(ContextTargetKind kind, string panelFolder, string entry) =>
        new(kind, PanelProviderKind.Local, panelFolder, entry, []);
}
