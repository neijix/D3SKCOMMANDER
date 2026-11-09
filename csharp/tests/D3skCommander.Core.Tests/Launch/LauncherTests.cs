using D3skCommander.Core.Common;
using D3skCommander.Core.Launch;
using Xunit;

namespace D3skCommander.Core.Tests.Launch;

/// <summary>The six tests of v0.1 <c>src/core/launcher.rs</c>, plus argv, terminal and user-command rules.</summary>
public class LauncherTests
{
    [Fact]
    public void SplitHonoursQuotes()
    {
        Assert.Equal(["viewer", "--opt", "a b", "c"], CommandBuilder.Split("viewer --opt \"a b\" c"));
        Assert.Equal(["C:\\Program Files\\x.exe", "%s"], CommandBuilder.Split("\"C:\\Program Files\\x.exe\" %s"));
        Assert.Equal(["a", "", "b"], CommandBuilder.Split("a \"\" b"));
        Assert.Equal(["--f=x y"], CommandBuilder.Split("--f=\"x y\""));
        Assert.Empty(CommandBuilder.Split("   "));
    }

    [Fact]
    public void OncePerFileWithPlaceholder()
    {
        var commands = CommandBuilder.Build("edit -f %s -q", ["a", "b"], LaunchMode.OncePerFile);
        Assert.Equal(2, commands.Count);
        Assert.Equal("edit", commands[0].Program);
        Assert.Equal(["-f", "a", "-q"], commands[0].Arguments);
        Assert.Equal(["-f", "b", "-q"], commands[1].Arguments);
    }

    [Fact]
    public void AllAtOnceExpandsPlaceholderToSeparateArgs()
    {
        var commands = CommandBuilder.Build("tool %s", ["a b", "c"], LaunchMode.AllAtOnce);
        Assert.Single(commands);
        Assert.Equal("tool", commands[0].Program);
        Assert.Equal(["a b", "c"], commands[0].Arguments);
    }

    [Fact]
    public void NoPlaceholderAppendsFiles()
    {
        var all = CommandBuilder.Build("tool --x", ["a", "b"], LaunchMode.AllAtOnce);
        Assert.Equal(["--x", "a", "b"], all[0].Arguments);
        var each = CommandBuilder.Build("tool", ["a", "b"], LaunchMode.OncePerFile);
        Assert.Equal(2, each.Count);
        Assert.Equal(["b"], each[1].Arguments);
    }

    [Fact]
    public void PlaceholderInsideAnArgument()
    {
        var commands = CommandBuilder.Build("t --file=%s", ["a", "b"], LaunchMode.AllAtOnce);
        Assert.Equal(["--file=a", "--file=b"], commands[0].Arguments);
    }

    [Fact]
    public void EmptyInputsGiveNoCommands()
    {
        Assert.Empty(CommandBuilder.Build("", ["a"], LaunchMode.AllAtOnce));
        Assert.Empty(CommandBuilder.Build("tool", [], LaunchMode.OncePerFile));
    }

    [Fact]
    public void ProgramWordIsNeverSubstituted()
    {
        var commands = CommandBuilder.Build("%s --file=%s", ["a"], LaunchMode.OncePerFile);
        Assert.Equal("%s", commands[0].Program);
        Assert.Equal(["--file=a"], commands[0].Arguments);
    }

    [Fact]
    public void ChosenProgramIsArgvNotAShellString()
    {
        var runner = new RecordingRunner();
        var result = Launcher.Launch(
            "\"C:\\Program Files\\x.exe\" %s",
            ["a b"],
            LaunchMode.OncePerFile,
            "/work",
            runner);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Started);
        var argv = Assert.IsType<ProcessRequest.Argv>(Assert.Single(runner.Requests));
        Assert.Equal("C:\\Program Files\\x.exe", argv.Program);
        Assert.Equal(["a b"], argv.Arguments);
        Assert.False(argv.UseShellExecute);
        Assert.Equal("/work", argv.WorkingDirectory);
    }

    [Fact]
    public void EmptyLaunchIsAnError()
    {
        var result = Launcher.Launch("  ", ["a"], LaunchMode.AllAtOnce, "/work", new RecordingRunner());
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.InvalidData, result.Error!.Kind);
        Assert.Equal("nothing to launch: empty command or no files", result.Error.Message);
    }

    [Fact]
    public void LaunchStopsWhenAProgramFailsToStart()
    {
        var runner = new RecordingRunner();
        runner.FailPrograms.Add("edit");
        var result = Launcher.Launch("edit %s", ["a", "b"], LaunchMode.OncePerFile, "/work", runner);
        Assert.False(result.IsSuccess);
        Assert.StartsWith("edit:", result.Error!.Message);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public void UserCommandPlaceholdersFollowV01()
    {
        var runner = new RecordingRunner();
        var command = new UserCommand("Edit each", "edit -f %s", oncePerFile: true);
        var result = Launcher.Launch(command, ["a", "b"], "/work", runner);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Started);
        var first = Assert.IsType<ProcessRequest.Argv>(runner.Requests[0]);
        var second = Assert.IsType<ProcessRequest.Argv>(runner.Requests[1]);
        Assert.Equal(["-f", "a"], first.Arguments);
        Assert.Equal(["-f", "b"], second.Arguments);
    }

    [Fact]
    public void CommandLineGoesThroughTheShell()
    {
        var runner = new RecordingRunner();
        var result = Launcher.RunInShell("echo \"hi there\"", "/work", runner);
        Assert.True(result.IsSuccess);
        var shell = Assert.IsType<ProcessRequest.Shell>(Assert.Single(runner.Requests));
        Assert.Equal("echo \"hi there\"", shell.CommandLine);
        Assert.Equal("/work", shell.WorkingDirectory);
    }

    [Fact]
    public void EmptyCommandLineIsAnError()
    {
        var runner = new RecordingRunner();
        var result = Launcher.RunInShell("   ", "/work", runner);
        Assert.False(result.IsSuccess);
        Assert.Equal("empty command line", result.Error!.Message);
        Assert.Equal(ErrorKind.InvalidData, result.Error.Kind);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void ArgvStartInfoKeepsEachArgumentWhole()
    {
        var info = ProcessRunner.CreateStartInfo(
            new ProcessRequest.Argv("tool", ["a b", "c"], "/work", false, false));
        Assert.Equal("tool", info.FileName);
        Assert.Equal("/work", info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
        Assert.Equal(["a b", "c"], info.ArgumentList);
        Assert.True(info.RedirectStandardOutput);
    }

    [Fact]
    public void ShellStartInfoPassesTheLineToTheShell()
    {
        var info = ProcessRunner.CreateStartInfo(new ProcessRequest.Shell("echo \"a b\"", "/work"));
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardInput);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), info.FileName);
            Assert.Equal("/C echo \"a b\"", info.Arguments);
            return;
        }

        Assert.Equal("sh", info.FileName);
        Assert.Equal(["-c", "echo \"a b\""], info.ArgumentList);
    }

    [Fact]
    public void DefaultOpenIsArgvOnUnixAndShellExecuteOnWindows()
    {
        var linux = Assert.IsType<ProcessRequest.Argv>(Launcher.DefaultOpenRequest("/tmp/a.txt", LaunchPlatform.Linux));
        Assert.Equal("xdg-open", linux.Program);
        Assert.Equal(["/tmp/a.txt"], linux.Arguments);
        Assert.False(linux.UseShellExecute);

        var mac = Assert.IsType<ProcessRequest.Argv>(Launcher.DefaultOpenRequest("/tmp/a.txt", LaunchPlatform.MacOS));
        Assert.Equal("open", mac.Program);
        Assert.Equal(["/tmp/a.txt"], mac.Arguments);
        Assert.False(mac.UseShellExecute);

        var windows = Assert.IsType<ProcessRequest.Argv>(
            Launcher.DefaultOpenRequest("C:\\temp\\a.txt", LaunchPlatform.Windows));
        Assert.Equal("C:\\temp\\a.txt", windows.Program);
        Assert.Empty(windows.Arguments);
        Assert.True(windows.UseShellExecute);
    }

    [Fact]
    public void LinuxTerminalListIsV01()
    {
        Assert.Equal(
            ["x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "alacritty", "kitty", "xterm"],
            Launcher.LinuxTerminals);
    }

    [Fact]
    public void LinuxTerminalTriesTheListInOrder()
    {
        var runner = new RecordingRunner();
        runner.FailPrograms.UnionWith(
            ["x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "alacritty"]);
        var result = Launcher.OpenTerminal("  ", "/work", runner, LaunchPlatform.Linux, NoResolve);
        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "alacritty", "kitty"],
            Programs(runner));
        var started = Assert.IsType<ProcessRequest.Argv>(runner.Requests[^1]);
        Assert.True(started.InheritStdio);
        Assert.Equal("/work", started.WorkingDirectory);
    }

    [Fact]
    public void LinuxTerminalReportsWhenNoneStart()
    {
        var runner = new RecordingRunner { FailAll = true };
        var result = Launcher.OpenTerminal(null, "/work", runner, LaunchPlatform.Linux, NoResolve);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(LaunchRefusal.NoTerminalFound, result.Refusal);
        Assert.Equal(Launcher.LinuxTerminals, Programs(runner));
    }

    [Fact]
    public void ConfiguredTerminalRunsInTheShell()
    {
        var runner = new RecordingRunner();
        var result = Launcher.OpenTerminal("  kitty -1  ", "/work", runner, LaunchPlatform.Linux, NoResolve);
        Assert.True(result.IsSuccess);
        var shell = Assert.IsType<ProcessRequest.Shell>(Assert.Single(runner.Requests));
        Assert.Equal("kitty -1", shell.CommandLine);
        Assert.Equal("/work", shell.WorkingDirectory);
    }

    [Fact]
    public void WindowsDefaultTerminalIsTheSystemCmdStartedByFullPath()
    {
        var runner = new RecordingRunner();
        var result = Launcher.OpenTerminal("", @"C:\work", runner, LaunchPlatform.Windows, NoResolve);
        Assert.True(result.IsSuccess);
        var argv = Assert.IsType<ProcessRequest.Argv>(Assert.Single(runner.Requests));
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.Equal(cmd, argv.Program);
        Assert.Equal(["/c", "start", cmd], argv.Arguments);
        Assert.True(argv.InheritStdio);
        Assert.False(argv.UseShellExecute);
        Assert.Equal(@"C:\work", argv.WorkingDirectory);
    }

    [Fact]
    public void WindowsOverrideBareProgramIsReplacedByItsFullPathAndTheRestIsKept()
    {
        var runner = new RecordingRunner();
        var asked = new List<string>();
        var result = Launcher.OpenTerminal("  wt  -d  \"a b\" ", @"C:\work", runner, LaunchPlatform.Windows, name =>
        {
            asked.Add(name);
            return @"C:\Tools\wt.cmd";
        });
        Assert.True(result.IsSuccess);
        Assert.Equal(["wt"], asked);
        var shell = Assert.IsType<ProcessRequest.Shell>(Assert.Single(runner.Requests));
        Assert.Equal(@"""""C:\Tools\wt.cmd""  -d  ""a b""""", shell.CommandLine);
        Assert.Equal(@"C:\work", shell.WorkingDirectory);
    }

    [Fact]
    public void WindowsOverrideQuotedProgramIsResolvedWithoutItsQuotes()
    {
        var runner = new RecordingRunner();
        var asked = new List<string>();
        Launcher.OpenTerminal("\"my term\" --x", @"C:\work", runner, LaunchPlatform.Windows, name =>
        {
            asked.Add(name);
            return @"C:\Tools\my term.exe";
        });
        Assert.Equal(["my term"], asked);
        var shell = Assert.IsType<ProcessRequest.Shell>(Assert.Single(runner.Requests));
        Assert.Equal(@"""""C:\Tools\my term.exe"" --x""", shell.CommandLine);
    }

    [Fact]
    public void WindowsOverrideWithAFullyQualifiedProgramIsWrappedLikeAResolvedOne()
    {
        var runner = new RecordingRunner();
        var program = Path.Combine(Path.GetTempPath(), "my term.exe");
        var result = Launcher.OpenTerminal("\"" + program + "\" -d \"C:\\x y\"", @"C:\work", runner, LaunchPlatform.Windows, _ => throw new InvalidOperationException("no lookup"));
        Assert.True(result.IsSuccess);
        var shell = Assert.IsType<ProcessRequest.Shell>(Assert.Single(runner.Requests));
        Assert.Equal("\"\"" + program + "\" -d \"C:\\x y\"\"", shell.CommandLine);
    }

    [Fact]
    public void WindowsOverrideWithAPercentInTheProgramPathIsRefused()
    {
        var runner = new RecordingRunner();
        var given = Path.Combine(Path.GetTempPath(), "100%.exe");
        var result = Launcher.OpenTerminal("\"" + given + "\" --x", @"C:\work", runner, LaunchPlatform.Windows, _ => throw new InvalidOperationException("no lookup"));
        Assert.Equal(LaunchRefusal.ProgramNotFound, result.Refusal);

        result = Launcher.OpenTerminal("tty --x", @"C:\work", runner, LaunchPlatform.Windows, _ => Path.Combine(Path.GetTempPath(), "a%b.exe"));
        Assert.Equal(LaunchRefusal.ProgramNotFound, result.Refusal);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void WindowsOverrideResolvedToANonFullyQualifiedPathIsRefused()
    {
        var runner = new RecordingRunner();
        var result = Launcher.OpenTerminal("tty --x", @"C:\work", runner, LaunchPlatform.Windows, _ => "tty.exe");
        Assert.Equal(LaunchRefusal.ProgramNotFound, result.Refusal);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void WindowsOverrideWhoseProgramDoesNotResolveIsRefusedAndNothingStarts()
    {
        var runner = new RecordingRunner();
        var result = Launcher.OpenTerminal("nosuchterm --x", @"C:\work", runner, LaunchPlatform.Windows, NoResolve);
        Assert.True(result.IsRefused);
        Assert.Equal(LaunchRefusal.ProgramNotFound, result.Refusal);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void MacDefaultTerminalIsOpenTerminal()
    {
        var runner = new RecordingRunner();
        var result = Launcher.OpenTerminal(null, "/work", runner, LaunchPlatform.MacOS, NoResolve);
        Assert.True(result.IsSuccess);
        var argv = Assert.IsType<ProcessRequest.Argv>(Assert.Single(runner.Requests));
        Assert.Equal("open", argv.Program);
        Assert.Equal(["-a", "Terminal", "/work"], argv.Arguments);
        Assert.True(argv.InheritStdio);
        Assert.Equal("/work", argv.WorkingDirectory);
    }

    [Fact]
    public void RealRunnerStartsAShellLineAndReturns()
    {
        new ProcessRunner().Start(new ProcessRequest.Shell("exit 0", Environment.CurrentDirectory));
    }

    private static string? NoResolve(string program) => null;

    private static List<string> Programs(RecordingRunner runner)
    {
        var names = new List<string>();
        foreach (var request in runner.Requests)
        {
            var argv = Assert.IsType<ProcessRequest.Argv>(request);
            names.Add(argv.Program);
        }

        return names;
    }
}
