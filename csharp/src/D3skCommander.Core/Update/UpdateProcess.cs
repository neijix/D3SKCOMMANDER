using System.Diagnostics;

namespace D3skCommander.Core.Update;

/// <summary>Starts a program and waits. Used for <c>pkexec</c> on Linux.</summary>
public sealed class UpdateProcess : IUpdateProcess
{
    public int Run(string program, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("pkexec runs on Linux.");
        }

        using var process = new Process();
        process.StartInfo.FileName = program;
        process.StartInfo.UseShellExecute = false;
        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new IOException("failed to start " + program);
        }

        process.WaitForExit();
        return process.ExitCode;
    }
}
