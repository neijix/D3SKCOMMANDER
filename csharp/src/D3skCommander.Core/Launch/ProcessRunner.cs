using System.Diagnostics;

namespace D3skCommander.Core.Launch;

/// <summary>
/// Starts a process and returns without waiting. A background wait reaps it.
/// Argv uses <see cref="ProcessStartInfo.ArgumentList"/>. The shell line is the exception:
/// <c>sh -c</c> on Unix, and <c>cmd /C</c> plus the line verbatim on Windows (v0.1 <c>raw_arg</c>).
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public void Start(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var process = StartProcess(request);
        if (process.StartInfo.RedirectStandardOutput)
        {
            DiscardOutput(process);
        }

        _ = Task.Run(() => WaitAndDispose(process));
    }

    public static ProcessStartInfo CreateStartInfo(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request is ProcessRequest.Shell shell)
        {
            return ShellInfo(shell);
        }

        if (request is ProcessRequest.Argv argv)
        {
            return ArgvInfo(argv);
        }

        if (request is ProcessRequest.Elevated elevated)
        {
            return new ProcessStartInfo
            {
                FileName = elevated.Program,
                Arguments = elevated.Arguments,
                WorkingDirectory = elevated.WorkingDirectory,
                UseShellExecute = true,
                Verb = "runas",
            };
        }

        throw new ArgumentException("Unknown process request.", nameof(request));
    }

    private static Process StartProcess(ProcessRequest request)
    {
        var process = new Process { StartInfo = CreateStartInfo(request), EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new IOException("failed to start " + process.StartInfo.FileName);
            }

            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static ProcessStartInfo ArgvInfo(ProcessRequest.Argv argv)
    {
        var info = new ProcessStartInfo
        {
            FileName = argv.Program,
            WorkingDirectory = argv.WorkingDirectory,
            UseShellExecute = argv.UseShellExecute,
            CreateNoWindow = !argv.InheritStdio && !argv.UseShellExecute,
        };
        if (!argv.UseShellExecute)
        {
            foreach (var argument in argv.Arguments)
            {
                info.ArgumentList.Add(argument);
            }
        }

        if (!argv.InheritStdio && !argv.UseShellExecute)
        {
            Detach(info);
        }

        return info;
    }

    private static ProcessStartInfo ShellInfo(ProcessRequest.Shell shell)
    {
        if (OperatingSystem.IsWindows())
        {
            var cmd = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                WorkingDirectory = shell.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = "/C " + shell.CommandLine,
            };
            Detach(cmd);
            return cmd;
        }

        var sh = new ProcessStartInfo
        {
            FileName = "sh",
            WorkingDirectory = shell.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        sh.ArgumentList.Add("-c");
        sh.ArgumentList.Add(shell.CommandLine);
        Detach(sh);
        return sh;
    }

    private static void Detach(ProcessStartInfo info)
    {
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
    }

    private static void DiscardOutput(Process process)
    {
        try
        {
            process.OutputDataReceived += IgnoreLine;
            process.ErrorDataReceived += IgnoreLine;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.StandardInput.Dispose();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The child already closed its streams.
        }
    }

    private static void IgnoreLine(object sender, DataReceivedEventArgs args)
    {
    }

    private static void WaitAndDispose(Process process)
    {
        try
        {
            process.WaitForExit();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The handle was already closed.
        }
        finally
        {
            process.Dispose();
        }
    }
}
