using System.Diagnostics;
using System.Security.Principal;

namespace D3skCommander.Core.FileSystem;

public sealed partial record EntryProperties
{
    private static readonly TimeSpan OwnerTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The owner's name, or null when the OS does not say. Unix asks <c>stat</c>; Windows reads the ACL.</summary>
    private static string? OwnerOf(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsOwner(path);
            }

            return OperatingSystem.IsMacOS() ? Stat(["-f", "%Su", path]) : Stat(["-c", "%U", path]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? WindowsOwner(string path)
    {
        var owner = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl().GetOwner(typeof(NTAccount))
            : new FileInfo(path).GetAccessControl().GetOwner(typeof(NTAccount));
        return owner?.ToString();
    }

    private static string? Stat(string[] arguments)
    {
        var start = new ProcessStartInfo("stat")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(OwnerTimeout))
        {
            process.Kill();
            return null;
        }

        var text = output.GetAwaiter().GetResult().Trim();
        return process.ExitCode == 0 && text.Length > 0 ? text : null;
    }
}
