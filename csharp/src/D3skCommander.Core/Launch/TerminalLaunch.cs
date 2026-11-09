using System.Text;

namespace D3skCommander.Core.Launch;

/// <summary>Which shell a terminal request opens.</summary>
public enum TerminalKind
{
    Terminal,
    PowerShell,
}

/// <summary>
/// Terminal and PowerShell in a folder, optionally elevated. Everything is argv except the elevated
/// cmd and Windows Terminal forms, whose verbatim arguments are built here (and refused when a folder
/// holds a character they cannot carry safely). The PATH probe and the platform are parameters.
/// </summary>
public static class TerminalLaunch
{
    private static readonly Dictionary<string, string[]> LinuxExec = new(StringComparer.Ordinal)
    {
        ["x-terminal-emulator"] = ["-e"],
        ["gnome-terminal"] = ["--"],
        ["konsole"] = ["-e"],
        ["xfce4-terminal"] = ["-x"],
        ["alacritty"] = ["-e"],
        ["kitty"] = [],
        ["xterm"] = ["-e"],
    };

    /// <summary>
    /// Opens <paramref name="kind"/> in <paramref name="directory"/>. <paramref name="configured"/> (the Settings
    /// terminal) applies only to a non-elevated <see cref="TerminalKind.Terminal"/>; <paramref name="resolve"/>
    /// answers the full path of a program (<c>wt</c>, <c>pwsh</c>) found on PATH, else null. Every Windows start passes a full path (a resolved one, or the system cmd.exe / powershell.exe); a configured terminal's first program is replaced by its resolved full path.
    /// </summary>
    public static LaunchResult Open(
        TerminalKind kind,
        bool elevated,
        string directory,
        string? configured,
        Func<string, string?> resolve,
        IProcessRunner runner,
        LaunchPlatform platform)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(runner);
        if (elevated && platform != LaunchPlatform.Windows)
        {
            throw new ArgumentException("Elevated terminals exist on Windows only.", nameof(elevated));
        }

        if (kind == TerminalKind.PowerShell)
        {
            return OpenPowerShell(elevated, directory, resolve, runner, platform);
        }

        if (!elevated && !string.IsNullOrWhiteSpace(configured))
        {
            return Launcher.OpenTerminal(configured, directory, runner, platform, resolve);
        }

        if (platform != LaunchPlatform.Windows)
        {
            return Launcher.OpenTerminal(null, directory, runner, platform, resolve);
        }

        return OpenWindowsTerminal(elevated, directory, resolve, runner);
    }

    private static LaunchResult OpenWindowsTerminal(bool elevated, string directory, Func<string, string?> resolve, IProcessRunner runner)
    {
        var wtPath = resolve("wt");
        var wt = wtPath is not null;
        if (!elevated)
        {
            ProcessRequest plain = wt
                ? new ProcessRequest.Argv(wtPath!, ["-d", directory.Replace(";", "\\;", StringComparison.Ordinal)], directory, true, false)
                : new ProcessRequest.Argv(CmdPath(), ["/c", "start", CmdPath()], directory, true, false);
            return Launcher.Start(plain, runner, null);
        }

        if (VerbatimRefusal(directory) is { } unsafeReason)
        {
            return LaunchResult.Refused(unsafeReason);
        }

        ProcessRequest request;
        if (wt)
        {
            var dir = directory.Replace(";", "\\;", StringComparison.Ordinal);
            if (dir.EndsWith('\\'))
            {
                dir += "\\";
            }

            request = new ProcessRequest.Elevated(wtPath!, "-d \"" + dir + "\"", directory);
        }
        else
        {
            request = new ProcessRequest.Elevated(CmdPath(), "/k cd /d \"" + directory + "\"", directory);
        }

        return Launcher.Start(request, runner, null);
    }

    /// <summary>The system cmd.exe by full path, so a same-named file on PATH or in the folder never wins.</summary>
    private static string CmdPath() => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static LaunchRefusal? VerbatimRefusal(string directory)
    {
        if (directory.Contains('%') || directory.Contains('!') || directory.Contains('"'))
        {
            return LaunchRefusal.UnsafeFolderName;
        }

        return null;
    }

    private static LaunchResult OpenPowerShell(bool elevated, string directory, Func<string, string?> resolve, IProcessRunner runner, LaunchPlatform platform)
    {
        if (platform == LaunchPlatform.MacOS)
        {
            throw new ArgumentException("PowerShell has no terminal entry on macOS.", nameof(platform));
        }

        var pwshPath = resolve("pwsh");
        var pwsh = pwshPath is not null;
        if (platform == LaunchPlatform.Linux)
        {
            return pwsh
                ? LinuxPowerShell(directory, runner)
                : LaunchResult.Refused(LaunchRefusal.PowerShellNotFound);
        }

        // A full path either way, so a planted name in the current folder is never run (as administrator, too).
        var program = pwshPath
            ?? resolve("powershell.exe")
            ?? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (elevated)
        {
            var arguments = "-NoExit -EncodedCommand " + EncodedSetLocation(directory);
            return Launcher.Start(new ProcessRequest.Elevated(program, arguments, directory), runner, null);
        }

        string[] args = pwsh
            ? ["-NoExit", "-WorkingDirectory", directory]
            : ["-NoExit", "-EncodedCommand", EncodedSetLocation(directory)];
        return Launcher.Start(new ProcessRequest.Argv(program, args, directory, true, false), runner, null);
    }

    private static LaunchResult LinuxPowerShell(string directory, IProcessRunner runner)
    {
        string[] shell = ["pwsh", "-NoExit", "-WorkingDirectory", directory];
        foreach (var candidate in Launcher.LinuxTerminals)
        {
            var exec = LinuxExec.TryGetValue(candidate, out var found) ? found : [];
            var request = new ProcessRequest.Argv(candidate, [.. exec, .. shell], directory, true, false);
            try
            {
                runner.Start(request);
                return LaunchResult.Ok(1);
            }
            catch (Exception)
            {
                // Like the plain terminal: try the next candidate whenever the spawn fails.
            }
        }

        return LaunchResult.Refused(LaunchRefusal.NoTerminalFound);
    }

    /// <summary>Base64 of UTF-16LE <c>Set-Location -LiteralPath '&lt;dir&gt;'</c>, every PowerShell quote doubled.</summary>
    internal static string EncodedSetLocation(string directory)
    {
        var literal = new StringBuilder();
        foreach (var c in directory)
        {
            literal.Append(c);
            if (c == '\'' || (c >= '‘' && c <= '‛'))
            {
                literal.Append(c);
            }
        }

        var script = "Set-Location -LiteralPath '" + literal + "'";
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }
}
