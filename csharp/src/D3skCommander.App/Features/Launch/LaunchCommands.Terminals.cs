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

    /// <summary>Whether a program (<c>wt</c>, <c>pwsh</c>) is found on PATH; tests replace it.</summary>
    public Func<string, bool> IsOnPath { get; set; } = ExecutablePath.IsOnPath;

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
        var isOnPath = IsOnPath;
        var result = await Task.Run(() => TerminalLaunch.Open(kind, elevated, directory, configured, isOnPath, _runner, platform)).ConfigureAwait(true);
        if (result.IsCancelled)
        {
            ShowStatus?.Invoke(_text.Get("launch.cancelled"));
        }
        else if (result.IsRefused)
        {
            ShowStatus?.Invoke(_text.Get("launch.refused") + ": " + result.Refusal);
        }
        else
        {
            Report(result);
        }
    }

    private string? TargetFolder()
    {
        if (_panels.ContextTarget is PanelContextTarget target)
        {
            return target.HereFolder;
        }

        return _panels.Capture()?.ActiveDirectory;
    }
}

/// <summary>Finds a program on PATH, the way a shell would (PATHEXT on Windows; a Windows app alias such as <c>wt.exe</c> counts).</summary>
internal static class ExecutablePath
{
    public static bool IsOnPath(string program)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        string[] extensions = [""];
        if (OperatingSystem.IsWindows())
        {
            var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM";
            extensions = ["", .. pathext.Split(';', StringSplitOptions.RemoveEmptyEntries)];
        }

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                if (ExistsSafe(Path.Combine(folder.Trim('"'), program + extension)))
                {
                    return true;
                }
            }
        }

        return false;
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
