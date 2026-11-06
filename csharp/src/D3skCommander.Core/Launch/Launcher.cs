using D3skCommander.Core.Common;

namespace D3skCommander.Core.Launch;

/// <summary>
/// v0.1 <c>launcher</c>: open with a chosen program or the default app, a terminal here,
/// and a command line in a folder. Arguments are argv. Only <see cref="RunInShell"/> is a shell line.
/// </summary>
public static class Launcher
{
    /// <summary>v0.1's Linux terminal list, first match wins.</summary>
    public static IReadOnlyList<string> LinuxTerminals { get; } =
    [
        "x-terminal-emulator",
        "gnome-terminal",
        "konsole",
        "xfce4-terminal",
        "alacritty",
        "kitty",
        "xterm",
    ];

    public static LaunchResult Launch(
        string command,
        IReadOnlyList<string> files,
        LaunchMode mode,
        string workingDirectory,
        IProcessRunner runner)
    {
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);
        ArgumentNullException.ThrowIfNull(runner);
        var commands = CommandBuilder.Build(command, files, mode);
        if (commands.Count == 0)
        {
            return LaunchResult.Fail(OperationError.InvalidData("nothing to launch: empty command or no files"));
        }

        var started = 0;
        foreach (var item in commands)
        {
            var request = new ProcessRequest.Argv(item.Program, item.Arguments, workingDirectory, false, false);
            var one = Start(request, runner, item.Program);
            if (!one.IsSuccess)
            {
                return one;
            }

            started++;
        }

        return LaunchResult.Ok(started);
    }

    public static LaunchResult Launch(UserCommand command, IReadOnlyList<string> files, string workingDirectory, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Launch(command.Command, files, command.Mode, workingDirectory, runner);
    }

    /// <summary>Starts each request in turn; the first failure stops the rest and is the result.</summary>
    public static LaunchResult Run(IReadOnlyList<ProcessRequest> requests, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(runner);
        var started = 0;
        foreach (var request in requests)
        {
            var one = Start(request, runner, request is ProcessRequest.Argv argv ? argv.Program : null);
            if (!one.IsSuccess)
            {
                return one;
            }

            started++;
        }

        return LaunchResult.Ok(started);
    }

    public static LaunchResult OpenWithDefault(string path, IProcessRunner runner) =>
        OpenWithDefault(path, runner, LaunchPlatforms.Current);

    public static LaunchResult OpenWithDefault(string path, IProcessRunner runner, LaunchPlatform platform)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(runner);
        return Start(DefaultOpenRequest(path, platform), runner, path);
    }

    public static ProcessRequest DefaultOpenRequest(string path, LaunchPlatform platform)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var cwd = ParentDirectory(path);
        if (platform == LaunchPlatform.Windows)
        {
            return new ProcessRequest.Argv(path, [], cwd, false, true);
        }

        if (platform == LaunchPlatform.MacOS)
        {
            return new ProcessRequest.Argv("open", [path], cwd, false, false);
        }

        if (platform == LaunchPlatform.Linux)
        {
            return new ProcessRequest.Argv("xdg-open", [path], cwd, false, false);
        }

        throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unknown platform.");
    }

    public static LaunchResult OpenTerminal(string? configured, string directory, IProcessRunner runner) =>
        OpenTerminal(configured, directory, runner, LaunchPlatforms.Current);

    /// <summary>
    /// A non-empty configured command runs in the shell in <paramref name="directory"/>.
    /// Otherwise the platform default: <c>cmd /c start cmd</c>, <c>open -a Terminal</c>,
    /// or the first Linux candidate that starts.
    /// </summary>
    public static LaunchResult OpenTerminal(string? configured, string directory, IProcessRunner runner, LaunchPlatform platform)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(runner);
        var custom = (configured ?? "").Trim();
        if (custom.Length > 0)
        {
            return RunInShell(custom, directory, runner);
        }

        return DefaultTerminal(directory, runner, platform);
    }

    public static LaunchResult RunInShell(string? commandLine, string workingDirectory, IProcessRunner runner)
    {
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);
        ArgumentNullException.ThrowIfNull(runner);
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return LaunchResult.Fail(OperationError.InvalidData("empty command line"));
        }

        return Start(new ProcessRequest.Shell(commandLine, workingDirectory), runner, commandLine);
    }

    private static LaunchResult DefaultTerminal(string directory, IProcessRunner runner, LaunchPlatform platform)
    {
        if (platform == LaunchPlatform.Windows)
        {
            var cmd = new ProcessRequest.Argv("cmd", ["/c", "start", "cmd"], directory, true, false);
            return Start(cmd, runner, null);
        }

        if (platform == LaunchPlatform.MacOS)
        {
            var open = new ProcessRequest.Argv("open", ["-a", "Terminal", directory], directory, true, false);
            return Start(open, runner, null);
        }

        if (platform != LaunchPlatform.Linux)
        {
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unknown platform.");
        }

        return FirstLinuxTerminal(directory, runner);
    }

    private static LaunchResult FirstLinuxTerminal(string directory, IProcessRunner runner)
    {
        foreach (var candidate in LinuxTerminals)
        {
            try
            {
                runner.Start(new ProcessRequest.Argv(candidate, [], directory, true, false));
                return LaunchResult.Ok(1);
            }
            catch (Exception)
            {
                // v0.1 tries the next name whenever spawn fails.
            }
        }

        return LaunchResult.Fail(OperationError.InvalidData("No terminal found: set one in the settings."));
    }

    private static LaunchResult Start(ProcessRequest request, IProcessRunner runner, string? prefix)
    {
        try
        {
            runner.Start(request);
            return LaunchResult.Ok(1);
        }
        catch (Exception ex)
        {
            var detail = string.IsNullOrEmpty(ex.Message) ? ex.GetType().Name : ex.Message;
            var message = prefix is null ? detail : prefix + ": " + detail;
            return LaunchResult.Fail(OperationError.FromMessage(message));
        }
    }

    private static string ParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
        {
            return Environment.CurrentDirectory;
        }

        return parent;
    }
}
