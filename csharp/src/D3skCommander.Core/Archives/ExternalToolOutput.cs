namespace D3skCommander.Core.Archives;

/// <summary>Stdout of one external archive tool, or why that process did not start.</summary>
public readonly struct ExternalToolOutput
{
    public ExternalToolOutput(int exitCode, string? standardOutput, string? standardError, string? startError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput ?? "";
        StandardError = standardError ?? "";
        StartError = startError;
    }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    /// <summary>Set when the process could not be started. <see cref="ExitCode"/> is unused then.</summary>
    public string? StartError { get; }

    public static ExternalToolOutput Ok(string standardOutput) => new(0, standardOutput, "", null);

    public static ExternalToolOutput FailedToStart(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            message = "failed to start";
        }

        return new ExternalToolOutput(-1, "", "", message);
    }
}
