using D3skCommander.Core.Launch;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.Launch;

/// <summary>TerminalHere, TerminalAdmin, PowerShellHere, PowerShellAdmin: the target's folder, the real PATH probe, status-line answers.</summary>
public sealed partial class LaunchCommands
{
    /// <summary>Shows a text on the main window's status line (set by the module); nothing when unset.</summary>
    public Action<string>? ShowStatus { get; set; }

    /// <summary>The platform the terminal commands decide for; tests change it.</summary>
    public LaunchPlatform Platform { get; set; } = LaunchPlatforms.Current;

    /// <summary>The full path of a program (<c>wt</c>, <c>pwsh</c>) found on PATH, else null; tests replace it.</summary>
    public Func<string, string?> ResolveProgram { get; set; } = program => ExecutablePath.Resolve(program);

    /// <summary>
    /// Opens a terminal or PowerShell in the context target's folder (the active folder outside a context menu).
    /// A cancelled or refused start shows a status text, a failure the launch-failed dialog.
    /// </summary>
    public async Task OpenTerminalHereAsync(TerminalKind kind, bool elevated)
    {
        if (elevated && Platform != LaunchPlatform.Windows)
        {
            ShowStatus?.Invoke(_text.Get("launch.windows_only"));
            return;
        }

        if (kind == TerminalKind.PowerShell && Platform == LaunchPlatform.MacOS)
        {
            ShowStatus?.Invoke(_text.Get("launch.powershell_unavailable"));
            return;
        }

        // The target is read before the first await: a context menu's scope ends with its dispatch.
        var directory = TargetFolder();
        if (directory is null)
        {
            return;
        }

        // Only a plain Terminal uses the Settings override.
        var configured = kind == TerminalKind.Terminal && !elevated ? _settings.Preferences.Current.Terminal : null;
        var platform = Platform;
        var resolve = ResolveProgram;
        var result = await Task.Run(() => TerminalLaunch.Open(kind, elevated, directory, configured, resolve, _runner, platform)).ConfigureAwait(true);
        Report(result);
    }

    /// <summary>The status text of a cancelled or refused start; null for a success or a failure (a dialog).</summary>
    private string? StatusFor(LaunchResult result)
    {
        if (result.IsCancelled)
        {
            return _text.Get("launch.cancelled");
        }

        return result.IsRefused ? _text.Get("launch.refused") + ": " + _text.Get(RefusalKey(result.Refusal!.Value)) : null;
    }

    private static string RefusalKey(LaunchRefusal refusal) => refusal switch
    {
        LaunchRefusal.UnsafeFolderName => "launch.refused.unsafe_folder",
        LaunchRefusal.PowerShellNotFound => "launch.refused.pwsh_missing",
        LaunchRefusal.ProgramNotFound => "launch.refused.program_missing",
        _ => "launch.refused.no_terminal",
    };

    /// <summary>
    /// The folder to open: the context target's, else the active folder; null (and a status text) when the active panel is an
    /// archive or a remote one, whose paths are no folders of this machine.
    /// </summary>
    private string? TargetFolder()
    {
        // A context target is checked like the active panel; none at all fails closed.
        var target = _panels.ContextTarget ?? _panels.ContextTargetFor(_panels.ActiveSide, false);
        if (target is not { Provider: PanelProviderKind.Local or PanelProviderKind.FlatList })
        {
            ShowStatus?.Invoke(_text.Get("launch.panel_unavailable"));
            return null;
        }

        return _panels.ContextTarget is not null ? target.HereFolder : _panels.Capture()?.ActiveDirectory;
    }
}

/// <summary>Finds a program on PATH, the way a shell would (PATHEXT on Windows; a Windows app alias such as <c>wt.exe</c> counts).</summary>
internal static class ExecutablePath
{
    public static bool IsOnPath(string program) => Resolve(program) is not null;

    /// <summary>The full path of the first match on the process PATH (see the overload), else null.</summary>
    public static string? Resolve(string program) =>
        Resolve(program, Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT"), OperatingSystem.IsWindows());

    /// <summary>
    /// The full path of the first match in <paramref name="path"/>, else null. Only fully qualified PATH entries count (a relative or
    /// drive-relative one would depend on the current folder). On Windows only a PATHEXT extension makes a match, in PATHEXT order, unless
    /// <paramref name="program"/> already ends in one: an extensionless file is no program there. Elsewhere the plain name is tried.
    /// </summary>
    public static string? Resolve(string program, string? path, string? pathExt, bool windows)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string[] extensions = [""];
        if (windows)
        {
            string[] known = (pathExt ?? ".EXE;.CMD;.BAT;.COM").Split(';', StringSplitOptions.RemoveEmptyEntries);
            var given = Path.GetExtension(program);
            extensions = given.Length > 0 && known.Contains(given, StringComparer.OrdinalIgnoreCase) ? [""] : known;
        }

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = folder.Trim('"');
            if (!Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, program + extension);
                if (ExistsSafe(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool ExistsSafe(string file)
    {
        try
        {
            return File.Exists(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
