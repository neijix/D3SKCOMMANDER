using System.ComponentModel;
using System.Text;
using D3skCommander.Core.Launch;
using Xunit;

namespace D3skCommander.Core.Tests.Launch;

public class TerminalLaunchTests
{
    private const LaunchPlatform Win = LaunchPlatform.Windows;

    private static Func<string, bool> Path(params string[] found) => name => found.Contains(name);

    private static string Decode(string base64) => Encoding.Unicode.GetString(Convert.FromBase64String(base64));

    private static LaunchResult Open(RecordingRunner runner, TerminalKind kind, bool elevated, string dir, Func<string, bool> path,
        LaunchPlatform platform = Win, string? configured = null) =>
        TerminalLaunch.Open(kind, elevated, dir, configured, path, runner, platform);

    [Fact]
    public void WindowsTerminalUsesWtWhenOnPathElseCmd()
    {
        var runner = new RecordingRunner();
        Assert.True(Open(runner, TerminalKind.Terminal, false, @"C:\My Docs;x", Path("wt")).IsSuccess);
        var wt = Assert.IsType<ProcessRequest.Argv>(runner.Requests[0]);
        Assert.Equal("wt", wt.Program);
        Assert.Equal(["-d", @"C:\My Docs\;x"], wt.Arguments);

        Assert.True(Open(runner, TerminalKind.Terminal, false, @"C:\a&b^c%d!e'f’g(h)", Path()).IsSuccess);
        var cmd = Assert.IsType<ProcessRequest.Argv>(runner.Requests[1]);
        Assert.Equal("cmd", cmd.Program);
        Assert.Equal(["/c", "start", "cmd"], cmd.Arguments);
        Assert.Equal(@"C:\a&b^c%d!e'f’g(h)", cmd.WorkingDirectory);
    }

    [Fact]
    public void TerminalHereRunsTheOverrideInTheShellButAdminAndPowerShellIgnoreIt()
    {
        var runner = new RecordingRunner();
        Open(runner, TerminalKind.Terminal, false, @"C:\d", Path("wt"), configured: "mytty --x");
        var shell = Assert.IsType<ProcessRequest.Shell>(runner.Requests[0]);
        Assert.Equal("mytty --x", shell.CommandLine);
        Assert.Equal(@"C:\d", shell.WorkingDirectory);

        Open(runner, TerminalKind.Terminal, true, @"C:\d", Path(), configured: "mytty");
        Assert.IsType<ProcessRequest.Elevated>(runner.Requests[1]);
        Open(runner, TerminalKind.PowerShell, false, @"C:\d", Path("pwsh"), configured: "mytty");
        Assert.Equal("pwsh", Assert.IsType<ProcessRequest.Argv>(runner.Requests[2]).Program);
    }

    [Fact]
    public void PwshUsesWorkingDirectoryAndWindowsPowerShellAnEncodedCommand()
    {
        var runner = new RecordingRunner();
        Open(runner, TerminalKind.PowerShell, false, @"C:\My Docs", Path("pwsh"));
        var pwsh = Assert.IsType<ProcessRequest.Argv>(runner.Requests[0]);
        Assert.Equal("pwsh", pwsh.Program);
        Assert.Equal(["-NoExit", "-WorkingDirectory", @"C:\My Docs"], pwsh.Arguments);

        const string dir = @"C:\a b&^%!'’‘‚‛();x";
        Open(runner, TerminalKind.PowerShell, false, dir, Path());
        var legacy = Assert.IsType<ProcessRequest.Argv>(runner.Requests[1]);
        Assert.Equal("powershell.exe", legacy.Program);
        Assert.Equal(["-NoExit", "-EncodedCommand"], legacy.Arguments.Take(2));
        Assert.Equal(@"Set-Location -LiteralPath 'C:\a b&^%!''’’‘‘‚‚‛‛();x'", Decode(legacy.Arguments[2]));
    }

    [Fact]
    public void ElevatedPowerShellRunsAsWithAnEncodedCommandForPwshAndPowerShellExe()
    {
        foreach (var (path, program) in new[] { (Path("pwsh"), "pwsh"), (Path(), "powershell.exe") })
        {
            var runner = new RecordingRunner();
            Assert.True(Open(runner, TerminalKind.PowerShell, true, @"C:\it's", path).IsSuccess);
            var request = Assert.IsType<ProcessRequest.Elevated>(Assert.Single(runner.Requests));
            Assert.Equal(program, request.Program);
            var info = ProcessRunner.CreateStartInfo(request);
            Assert.Equal("runas", info.Verb);
            Assert.True(info.UseShellExecute);
            Assert.StartsWith("-NoExit -EncodedCommand ", info.Arguments, StringComparison.Ordinal);
            Assert.Equal(@"Set-Location -LiteralPath 'C:\it''s'", Decode(info.Arguments["-NoExit -EncodedCommand ".Length..]));
        }
    }

    [Theory]
    [InlineData(@"C:\a b", @"/k cd /d ""C:\a b""", @"-d ""C:\a b""")]
    [InlineData(@"C:\a&b", @"/k cd /d ""C:\a&b""", @"-d ""C:\a&b""")]
    [InlineData(@"C:\a^b", @"/k cd /d ""C:\a^b""", @"-d ""C:\a^b""")]
    [InlineData(@"C:\a(b)", @"/k cd /d ""C:\a(b)""", @"-d ""C:\a(b)""")]
    [InlineData(@"C:\a;b", @"/k cd /d ""C:\a;b""", @"-d ""C:\a\;b""")]
    [InlineData(@"C:\", @"/k cd /d ""C:\""", @"-d ""C:\\""")]
    public void ElevatedCmdAndWtGetTheVerbatimQuotedArguments(string dir, string cmdArgs, string wtArgs)
    {
        var cmdRunner = new RecordingRunner();
        Open(cmdRunner, TerminalKind.Terminal, true, dir, Path());
        var cmd = ProcessRunner.CreateStartInfo(Assert.Single(cmdRunner.Requests));
        Assert.Equal("cmd", cmd.FileName);
        Assert.Equal("runas", cmd.Verb);
        Assert.Equal(cmdArgs, cmd.Arguments);

        var wtRunner = new RecordingRunner();
        Open(wtRunner, TerminalKind.Terminal, true, dir, Path("wt"));
        var wt = ProcessRunner.CreateStartInfo(Assert.Single(wtRunner.Requests));
        Assert.Equal("wt", wt.FileName);
        Assert.Equal("runas", wt.Verb);
        Assert.Equal(wtArgs, wt.Arguments);
    }

    [Theory]
    [InlineData(@"C:\50%")]
    [InlineData(@"C:\hey!")]
    public void ElevatedCmdAndWtRefusePercentAndBangAndStartNothing(string dir)
    {
        foreach (var path in new[] { Path(), Path("wt") })
        {
            var runner = new RecordingRunner();
            var result = Open(runner, TerminalKind.Terminal, true, dir, path);
            Assert.True(result.IsRefused);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Error);
            Assert.False(string.IsNullOrEmpty(result.Refusal));
            Assert.Empty(runner.Requests);
        }
    }

    [Fact]
    public void ACancelledPromptIsCancelledAndOtherErrorsFail()
    {
        var runner = new RecordingRunner { Throw = new Win32Exception(1223) };
        var cancelled = Open(runner, TerminalKind.PowerShell, true, @"C:\d", Path("pwsh"));
        Assert.True(cancelled.IsCancelled);
        Assert.False(cancelled.IsSuccess);
        Assert.Null(cancelled.Error);

        runner.Throw = new Win32Exception(2, "The system cannot find the file specified");
        var failed = Open(runner, TerminalKind.PowerShell, true, @"C:\d", Path("pwsh"));
        Assert.False(failed.IsCancelled);
        Assert.NotNull(failed.Error);
    }

    [Fact]
    public void LinuxPowerShellUsesTheExecArgumentsOfTheFirstTerminalThatStarts()
    {
        var expected = new (string Program, string[] Exec)[]
        {
            ("x-terminal-emulator", ["-e"]),
            ("gnome-terminal", ["--"]),
            ("konsole", ["-e"]),
            ("xfce4-terminal", ["-x"]),
            ("alacritty", ["-e"]),
            ("kitty", []),
            ("xterm", ["-e"]),
        };
        Assert.Equal(Launcher.LinuxTerminals, expected.Select(e => e.Program));
        for (var i = 0; i < expected.Length; i++)
        {
            var runner = new RecordingRunner();
            foreach (var earlier in expected.Take(i))
            {
                runner.FailPrograms.Add(earlier.Program);
            }

            Assert.True(Open(runner, TerminalKind.PowerShell, false, "/home/a b", Path("pwsh"), LaunchPlatform.Linux).IsSuccess);
            var last = Assert.IsType<ProcessRequest.Argv>(runner.Requests[^1]);
            Assert.Equal(expected[i].Program, last.Program);
            Assert.Equal([.. expected[i].Exec, "pwsh", "-NoExit", "-WorkingDirectory", "/home/a b"], last.Arguments);
        }
    }

    [Fact]
    public void LinuxWithoutPwshFailsAndNothingStarts()
    {
        var runner = new RecordingRunner();
        var result = Open(runner, TerminalKind.PowerShell, false, "/h", Path(), LaunchPlatform.Linux);
        Assert.NotNull(result.Error);
        Assert.Contains("pwsh not found", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void PlatformMismatchesAreArgumentErrors()
    {
        var runner = new RecordingRunner();
        Assert.Throws<ArgumentException>(() => Open(runner, TerminalKind.PowerShell, false, "/h", Path("pwsh"), LaunchPlatform.MacOS));
        Assert.Throws<ArgumentException>(() => Open(runner, TerminalKind.Terminal, true, "/h", Path(), LaunchPlatform.MacOS));
        Assert.Throws<ArgumentException>(() => Open(runner, TerminalKind.PowerShell, true, "/h", Path("pwsh"), LaunchPlatform.Linux));
    }

    [Fact]
    public void MacAndLinuxTerminalKeepTodaysDefaults()
    {
        var mac = new RecordingRunner();
        Open(mac, TerminalKind.Terminal, false, "/Users/a", Path(), LaunchPlatform.MacOS);
        var open = Assert.IsType<ProcessRequest.Argv>(Assert.Single(mac.Requests));
        Assert.Equal("open", open.Program);
        Assert.Equal(["-a", "Terminal", "/Users/a"], open.Arguments);

        var linux = new RecordingRunner();
        Open(linux, TerminalKind.Terminal, false, "/h", Path(), LaunchPlatform.Linux);
        Assert.Equal("x-terminal-emulator", Assert.IsType<ProcessRequest.Argv>(Assert.Single(linux.Requests)).Program);
    }
}
