using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace D3skCommander.Core.Workspace;

/// <summary>
/// Hands out one folder under <c>{temp}/d3skcommander</c> (the v0.1 extract root).
/// Dispose removes it. Construction removes leftovers whose process is gone.
/// </summary>
public sealed class TempWorkspace : ITempWorkspace
{
    public const string RootFolderName = "d3skcommander";

    /// <summary>The process temp root. A private root can be passed to the constructor in tests.</summary>
    public static string DefaultRoot => Path.Combine(Path.GetTempPath(), RootFolderName);

    private int _disposed;

    public TempWorkspace()
        : this(DefaultRoot)
    {
    }

    public TempWorkspace(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var full = Path.GetFullPath(root);
        Directory.CreateDirectory(full);
        SweepLeftovers(full);
        SessionFolder = Path.Combine(full, SessionName(Environment.ProcessId));
        Directory.CreateDirectory(SessionFolder);
    }

    public string SessionFolder { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(SessionFolder))
            {
                Directory.Delete(SessionFolder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Exit cleanup is best-effort. A locked file stays until a later start
            // sees this process id as dead.
        }
    }

    private static void SweepLeftovers(string root)
    {
        foreach (var entry in Directory.EnumerateDirectories(root))
        {
            // A live pid is another instance, or another session in this process. Leave it.
            if (!TryReadOwnerPid(Path.GetFileName(entry), out var pid) || IsAlive(pid))
            {
                continue;
            }

            try
            {
                Directory.Delete(entry, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Retried the next time a workspace is created.
            }
        }
    }

    /// <summary><c>{pid}</c> is v0.1. <c>{pid}-{32 hex}</c> is one session of this process.</summary>
    private static bool TryReadOwnerPid(string name, out int pid)
    {
        pid = 0;
        var dash = name.IndexOf('-');
        if (dash >= 0 && !IsSessionSuffix(name[(dash + 1)..]))
        {
            return false;
        }

        var pidText = dash < 0 ? name : name[..dash];
        return int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0;
    }

    private static bool IsSessionSuffix(string suffix)
    {
        if (suffix.Length != 32)
        {
            return false;
        }

        foreach (var c in suffix)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string SessionName(int pid) => $"{pid}-{Guid.NewGuid():N}";

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            // Cannot tell (another user's process). Keeping the folder is the safe side.
            return true;
        }
    }
}
