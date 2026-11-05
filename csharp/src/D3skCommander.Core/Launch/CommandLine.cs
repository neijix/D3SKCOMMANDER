using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Launch;

/// <summary>What a command-line Enter does. v0.1 <c>run_command_line</c> decides this before any process starts.</summary>
public enum CommandLineKind
{
    None,
    Navigate,
    Error,
    Shell,
}

/// <summary>
/// <see cref="CommandLineKind.Navigate"/> carries <see cref="Path"/>.
/// <see cref="CommandLineKind.Error"/> carries <see cref="Error"/>.
/// <see cref="CommandLineKind.Shell"/> carries <see cref="Line"/> and <see cref="WorkingDirectory"/>.
/// </summary>
public sealed class CommandLineDecision
{
    private CommandLineDecision(
        CommandLineKind kind,
        string? path,
        string? line,
        string? workingDirectory,
        OperationError? error)
    {
        Kind = kind;
        Path = path;
        Line = line;
        WorkingDirectory = workingDirectory;
        Error = error;
    }

    public CommandLineKind Kind { get; }

    public string? Path { get; }

    public string? Line { get; }

    public string? WorkingDirectory { get; }

    public OperationError? Error { get; }

    public static CommandLineDecision None() => new(CommandLineKind.None, null, null, null, null);

    public static CommandLineDecision Navigate(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new CommandLineDecision(CommandLineKind.Navigate, path, null, null, null);
    }

    public static CommandLineDecision Missing(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new CommandLineDecision(CommandLineKind.Error, error.Path, null, null, error);
    }

    public static CommandLineDecision Shell(string line, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(line);
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);
        return new CommandLineDecision(CommandLineKind.Shell, null, line, workingDirectory, null);
    }
}

/// <summary>
/// v0.1 command line: <c>cd</c> with no argument, <c>~</c> or <c>~/…</c> goes home;
/// a relative or quoted path joins the active folder; a missing folder is an error;
/// any other line runs in the shell in the active folder (home when the panel has none).
/// </summary>
public static class CommandLine
{
    public static CommandLineDecision Interpret(string? line, string? activeDirectory, string? homeDirectory = null)
    {
        var home = string.IsNullOrEmpty(homeDirectory) ? LocalFileSystem.HomeDirectory() : homeDirectory;
        var active = string.IsNullOrEmpty(activeDirectory) ? home : activeDirectory;
        var text = (line ?? "").Trim();
        if (text.Length == 0)
        {
            return CommandLineDecision.None();
        }

        if (text == "cd" || text.StartsWith("cd ", StringComparison.Ordinal))
        {
            return ChangeDirectory(text, active, home);
        }

        return CommandLineDecision.Shell(text, active);
    }

    private static CommandLineDecision ChangeDirectory(string line, string active, string home)
    {
        var target = Target(Argument(line), active, home);
        if (!Directory.Exists(target) && !File.Exists(target))
        {
            return CommandLineDecision.Missing(OperationError.NotFound(target, FolderMessage(target)));
        }

        var canonical = Canonical(target);
        if (Directory.Exists(target))
        {
            return CommandLineDecision.Navigate(canonical);
        }

        return CommandLineDecision.Missing(OperationError.NotFound(canonical, FolderMessage(canonical)));
    }

    // v0.1: line[2..].trim().trim_matches('"') — quotes wrap the whole argument, they are not shell quotes.
    private static string Argument(string line)
    {
        if (line.Length <= 2)
        {
            return "";
        }

        return line[2..].Trim().Trim('"');
    }

    private static string Target(string arg, string active, string home)
    {
        if (arg.Length == 0 || arg == "~")
        {
            return home;
        }

        if (arg.StartsWith("~/", StringComparison.Ordinal))
        {
            return Join(home, arg[2..]);
        }

        if (Path.IsPathRooted(arg))
        {
            return arg;
        }

        return Join(active, arg);
    }

    // One segment at a time. On Windows "/" is a separator too, so "~/x/y" joins; ".." stays in the text.
    private static string Join(string root, string relative)
    {
        var path = root;
        var start = 0;
        for (var i = 0; i <= relative.Length; i++)
        {
            var boundary = i == relative.Length || IsSeparator(relative[i]);
            if (!boundary)
            {
                continue;
            }

            if (i > start)
            {
                path = Path.Combine(path, relative[start..i]);
            }

            start = i + 1;
        }

        return path;
    }

    private static bool IsSeparator(char c) =>
        c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

    private static string FolderMessage(string path) => "Folder: " + path;

    private static string Canonical(string path)
    {
        var info = new DirectoryInfo(path);
        try
        {
            var final = info.ResolveLinkTarget(returnFinalTarget: true);
            if (final is null)
            {
                return info.FullName;
            }

            if (Path.IsPathFullyQualified(final.FullName))
            {
                return final.FullName;
            }

            var parent = info.Parent is null ? Environment.CurrentDirectory : info.Parent.FullName;
            return Path.GetFullPath(Path.Combine(parent, final.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return info.FullName;
        }
    }
}
