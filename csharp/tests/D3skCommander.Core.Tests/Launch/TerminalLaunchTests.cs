using System.ComponentModel;
using System.Text;
using D3skCommander.Core.Launch;
using Xunit;

namespace D3skCommander.Core.Tests.Launch;

public class TerminalLaunchTests
{
    private const LaunchPlatform Win = LaunchPlatform.Windows;

    private static Func<string, string?> OnPath(params string[] found) =>
        name => found.Contains(name) ? Path.Combine(Path.GetTempPath(), name + ".exe") : null;

    private static string Decode(string base64) => Encoding.Unicode.GetString(Convert.FromBase64String(base64));

    private static LaunchResult Open(RecordingRunner runner, TerminalKind kind, bool elevated, string dir, Func<string, string?> path,
        LaunchPlatform platform = Win, string? configured = null) =>
        TerminalLaunch.Open(kind, elevated, dir, configured, path, runner, platform);

    [Fact]
    public void WindowsTerminalUsesWtWhenOnPathElseCmd()
    {
        var runner = new RecordingRunner();
        Assert.True(Open(runner, TerminalKind.Terminal, false, @"C:\My Docs;x", OnPath("wt")).IsSuccess);
        var wt = Assert.IsType<ProcessRequest.Argv>(runner.Requests[0]);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "wt.exe"), wt.Program);
        Assert.Equal(["-d", @"C:\My Docs\;x"], wt.Arguments);

        Assert.True(Open(runner, TerminalKind.Terminal, false, @"C:\a&b^c%d!e'f’g(h)", OnPath()).IsSuccess);
        var cmd = Assert.IsType<ProcessRequest.Argv>(runner.Requests[1]);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd.Program);
        Assert.Equal(["/c", "start", cmd.Program], cmd.Arguments);
        Assert.Equal(@"C:\a&b^c%d!e'f’g(h)", cmd.WorkingDirectory);
    }

    [Fact]
    public void TerminalHereRunsTheOverrideInTheShellButAdminAndPowerShellIgnoreIt()
    {
        var runner = new RecordingRunner();
        // A Windows full path whatever OS runs the test: the override is only qualified for Windows.
        Open(runner, TerminalKind.Terminal, false, @"C:\d", name => name == "mytty" ? @"C:\Tools\mytty.exe" : null, configured: "mytty --x");
        var shell = Assert.IsType<ProcessRequest.Shell>(runner.Requests[0]);
        Assert.Equal("\"\"" + @"C:\Tools\mytty.exe" + "\" --x\"", shell.CommandLine);
        Assert.Equal(@"C:\d", shell.WorkingDirectory);

        Open(runner, TerminalKind.Terminal, true, @"C:\d", OnPath(), configured: "mytty");
        Assert.IsType<ProcessRequest.Elevated>(runner.Requests[1]);
        Open(runner, TerminalKind.PowerShell, false, @"C:\d", OnPath("pwsh"), configured: "mytty");
        Assert.Equal("pwsh.exe", Path.GetFileName(Assert.IsType<ProcessRequest.Argv>(runner.Requests[2]).Program));
    }

    [Fact]
    public void PwshUsesWorkingDirectoryAndWindowsPowerShellAnEncodedCommand()
    {
        var runner = new RecordingRunner();
        Open(runner, TerminalKind.PowerShell, false, @"C:\My Docs", OnPath("pwsh"));
        var pwsh = Assert.IsType<ProcessRequest.Argv>(runner.Requests[0]);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "pwsh.exe"), pwsh.Program);
        Assert.Equal(["-NoExit", "-WorkingDirectory", @"C:\My Docs"], pwsh.Arguments);

        const string dir = @"C:\a b&^%!'’‘‚‛();x";
        Open(runner, TerminalKind.PowerShell, false, dir, OnPath());
        var legacy = Assert.IsType<ProcessRequest.Argv>(runner.Requests[1]);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), legacy.Program);
        Assert.Equal(["-NoExit", "-EncodedCommand"], legacy.Arguments.Take(2));
        Assert.Equal(@"Set-Location -LiteralPath 'C:\a b&^%!''’’‘‘‚‚‛‛();x'", Decode(legacy.Arguments[2]));
    }

    [Fact]
    public void ElevatedPowerShellRunsAsWithAnEncodedCommandForPwshAndPowerShellExe()
    {
        foreach (var (path, program) in new[] { (OnPath("pwsh"), "pwsh.exe"), (OnPath(), "powershell.exe") })
        {
            var runner = new RecordingRunner();
            Assert.True(Open(runner, TerminalKind.PowerShell, true, @"C:\it's", path).IsSuccess);
            var request = Assert.IsType<ProcessRequest.Elevated>(Assert.Single(runner.Requests));
            Assert.Equal(program, Path.GetFileName(request.Program));
            var info = ProcessRunner.CreateStartInfo(request);
            Assert.Equal("runas", info.Verb);
            Assert.True(info.UseShellExecute);
            Assert.StartsWith("-NoExit -EncodedCommand ", info.Arguments, StringComparison.Ordinal);
            Assert.Equal(@"Set-Location -LiteralPath 'C:\it''s'", Decode(info.Arguments["-NoExit -EncodedCommand ".Length..]));
        }
    }

    [Fact]
    public void EveryElevatedRequestHasAFullyQualifiedProgram()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The elevated forms and the system cmd.exe folder exist on Windows only");
        foreach (var kind in new[] { TerminalKind.Terminal, TerminalKind.PowerShell })
        {
            foreach (var path in new[] { OnPath(), OnPath("wt"), OnPath("pwsh"), OnPath("wt", "pwsh") })
            {
                var runner = new RecordingRunner();
                Assert.True(Open(runner, kind, true, @"C:\d", path).IsSuccess);
                var request = Assert.IsType<ProcessRequest.Elevated>(Assert.Single(runner.Requests));
                Assert.True(Path.IsPathFullyQualified(request.Program), request.Program);
            }
        }
    }

    [Fact]
    public void EveryNonElevatedWindowsRequestHasAFullyQualifiedProgram()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The system cmd.exe folder exists on Windows only");
        foreach (var kind in new[] { TerminalKind.Terminal, TerminalKind.PowerShell })
        {
            foreach (var path in new[] { OnPath(), OnPath("wt"), OnPath("pwsh"), OnPath("wt", "pwsh") })
            {
                var runner = new RecordingRunner();
                Assert.True(Open(runner, kind, false, @"C:\d", path).IsSuccess);
                var request = Assert.IsType<ProcessRequest.Argv>(Assert.Single(runner.Requests));
                Assert.True(Path.IsPathFullyQualified(request.Program), request.Program);
            }
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
        Open(cmdRunner, TerminalKind.Terminal, true, dir, OnPath());
        var cmd = ProcessRunner.CreateStartInfo(Assert.Single(cmdRunner.Requests));
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd.FileName);
        Assert.Equal("runas", cmd.Verb);
        Assert.Equal(cmdArgs, cmd.Arguments);

        var wtRunner = new RecordingRunner();
        Open(wtRunner, TerminalKind.Terminal, true, dir, OnPath("wt"));
        var wt = ProcessRunner.CreateStartInfo(Assert.Single(wtRunner.Requests));
        Assert.True(Path.IsPathRooted(wt.FileName));
        Assert.Equal("wt.exe", Path.GetFileName(wt.FileName));
        Assert.Equal("runas", wt.Verb);
        Assert.Equal(wtArgs, wt.Arguments);
    }

    [Theory]
    [InlineData(@"C:\50%")]
    [InlineData(@"C:\hey!")]
    public void ElevatedCmdAndWtRefusePercentAndBangAndStartNothing(string dir)
    {
        foreach (var path in new[] { OnPath(), OnPath("wt") })
        {
            var runner = new RecordingRunner();
            var result = Open(runner, TerminalKind.Terminal, true, dir, path);
            Assert.True(result.IsRefused);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Error);
            Assert.Equal(LaunchRefusal.UnsafeFolderName, result.Refusal);
            Assert.Empty(runner.Requests);
        }
    }

    [Fact]
    public void ACancelledPromptIsCancelledAndOtherErrorsFail()
    {
        var runner = new RecordingRunner { Throw = new Win32Exception(1223) };
        var cancelled = Open(runner, TerminalKind.PowerShell, true, @"C:\d", OnPath("pwsh"));
        Assert.True(cancelled.IsCancelled);
        Assert.False(cancelled.IsSuccess);
        Assert.Null(cancelled.Error);

        runner.Throw = new Win32Exception(2, "The system cannot find the file specified");
        var failed = Open(runner, TerminalKind.PowerShell, true, @"C:\d", OnPath("pwsh"));
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

            Assert.True(Open(runner, TerminalKind.PowerShell, false, "/home/a b", OnPath("pwsh"), LaunchPlatform.Linux).IsSuccess);
            var last = Assert.IsType<ProcessRequest.Argv>(runner.Requests[^1]);
            Assert.Equal(expected[i].Program, last.Program);
            Assert.Equal([.. expected[i].Exec, "pwsh", "-NoExit", "-WorkingDirectory", "/home/a b"], last.Arguments);
        }
    }

    [Fact]
    public void LinuxWithoutPwshIsRefusedAndNothingStarts()
    {
        var runner = new RecordingRunner();
        var result = Open(runner, TerminalKind.PowerShell, false, "/h", OnPath(), LaunchPlatform.Linux);
        Assert.Equal(LaunchRefusal.PowerShellNotFound, result.Refusal);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void PlatformMismatchesAreArgumentErrors()
    {
        var runner = new RecordingRunner();
        Assert.Throws<ArgumentException>(() => Open(runner, TerminalKind.PowerShell, false, "/h", OnPath("pwsh"), LaunchPlatform.MacOS));
        Assert.Throws<ArgumentException>(() => Open(runner, TerminalKind.Terminal, true, "/h", OnPath(), LaunchPlatform.MacOS));
        Assert.Throws<ArgumentException>(() => Open(runner, TerminalKind.PowerShell, true, "/h", OnPath("pwsh"), LaunchPlatform.Linux));
    }

    [Fact]
    public void MacAndLinuxTerminalKeepTodaysDefaults()
    {
        var mac = new RecordingRunner();
        Open(mac, TerminalKind.Terminal, false, "/Users/a", OnPath(), LaunchPlatform.MacOS);
        var open = Assert.IsType<ProcessRequest.Argv>(Assert.Single(mac.Requests));
        Assert.Equal("open", open.Program);
        Assert.Equal(["-a", "Terminal", "/Users/a"], open.Arguments);

        var linux = new RecordingRunner();
        Open(linux, TerminalKind.Terminal, false, "/h", OnPath(), LaunchPlatform.Linux);
        Assert.Equal("x-terminal-emulator", Assert.IsType<ProcessRequest.Argv>(Assert.Single(linux.Requests)).Program);
    }
}
