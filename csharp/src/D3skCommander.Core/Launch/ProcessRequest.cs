namespace D3skCommander.Core.Launch;

/// <summary>
/// A process to start. <see cref="Argv"/> never goes through a shell.
/// <see cref="Shell"/> is only the command line itself (and a configured terminal, which is one).
/// </summary>
public abstract class ProcessRequest
{
    private ProcessRequest(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);
        WorkingDirectory = workingDirectory;
    }

    public string WorkingDirectory { get; }

    /// <summary>Program plus arguments. <see cref="UseShellExecute"/> is the Windows default-app open.</summary>
    public sealed class Argv : ProcessRequest
    {
        public Argv(
            string program,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            bool inheritStdio,
            bool useShellExecute)
            : base(workingDirectory)
        {
            ArgumentException.ThrowIfNullOrEmpty(program);
            ArgumentNullException.ThrowIfNull(arguments);
            if (useShellExecute && (inheritStdio || arguments.Count > 0))
            {
                throw new ArgumentException(
                    "A shell-execute open takes the document path as the program and no arguments.",
                    nameof(useShellExecute));
            }

            Program = program;
            Arguments = ArgumentsCopy.Of(arguments);
            InheritStdio = inheritStdio;
            UseShellExecute = useShellExecute;
        }

        public string Program { get; }

        public IReadOnlyList<string> Arguments { get; }

        public bool InheritStdio { get; }

        public bool UseShellExecute { get; }
    }

    /// <summary>One command line for <c>sh -c</c> or <c>cmd /C</c>. The line is not re-quoted.</summary>
    public sealed class Shell : ProcessRequest
    {
        public Shell(string commandLine, string workingDirectory)
            : base(workingDirectory)
        {
            ArgumentNullException.ThrowIfNull(commandLine);
            CommandLine = commandLine;
        }

        public string CommandLine { get; }
    }

    /// <summary>
    /// An elevated start: ShellExecute with verb <c>runas</c> (the UAC prompt). The arguments are one string
    /// passed verbatim, the one exception to argv; the caller quotes them (<see cref="TerminalLaunch"/>).
    /// </summary>
    public sealed class Elevated : ProcessRequest
    {
        public Elevated(string program, string arguments, string workingDirectory)
            : base(workingDirectory)
        {
            ArgumentException.ThrowIfNullOrEmpty(program);
            ArgumentNullException.ThrowIfNull(arguments);
            Program = program;
            Arguments = arguments;
        }

        public string Program { get; }

        public string Arguments { get; }
    }
}
