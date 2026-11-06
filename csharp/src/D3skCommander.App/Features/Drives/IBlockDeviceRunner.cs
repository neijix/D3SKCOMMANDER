using System.Diagnostics;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Features.Drives;

/// <summary>The outcome of one tool call: success, what it printed, what it complained about.</summary>
public readonly record struct ToolResult(bool Ok, string Stdout, string Stderr);

/// <summary>The disk tools (<c>lsblk</c>, <c>udisksctl</c>). Commands use this seam so tests never mount anything.</summary>
public interface IBlockDeviceRunner
{
    /// <summary>The <c>lsblk</c> JSON, or null when the tool is missing or this is not Linux.</summary>
    string? List();

    ToolResult Mount(string device);

    ToolResult Unmount(string device);

    ToolResult PowerOff(string device);
}

/// <summary>Runs the real tools: no shell, 15 s limit, a missing tool is a failed result, never an exception.</summary>
public sealed class ProcessBlockDeviceRunner : IBlockDeviceRunner
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    public string? List()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var result = Run("lsblk", BlockDevices.LsblkArguments);
        return result.Ok ? result.Stdout : null;
    }

    public ToolResult Mount(string device) => Udisks("mount", device);

    public ToolResult Unmount(string device) => Udisks("unmount", device);

    public ToolResult PowerOff(string device) => Udisks("power-off", device);

    private static ToolResult Udisks(string verb, string device) => Run("udisksctl", [verb, "-b", device]);

    private static ToolResult Run(string tool, IEnumerable<string> arguments)
    {
        try
        {
            var info = new ProcessStartInfo(tool)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return new ToolResult(false, "", tool);
            }

            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Limit))
            {
                process.Kill(true);
                return new ToolResult(false, "", tool + ": timeout");
            }

            return new ToolResult(process.ExitCode == 0, output.GetAwaiter().GetResult(), errors.GetAwaiter().GetResult().Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new ToolResult(false, "", tool + ": " + ex.Message);
        }
    }
}
