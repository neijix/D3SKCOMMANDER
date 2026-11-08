namespace D3skCommander.Core.Install;

/// <summary>What <see cref="InstallDetection.Detect"/> looks at. <see cref="Current"/> reads the running process.</summary>
public sealed class InstallFacts
{
    /// <summary><c>linux</c>, <c>macos</c> or <c>windows</c>, matching v0.1's <c>std::env::consts::OS</c>.</summary>
    public required string Os { get; init; }

    /// <summary>The program file, canonical when <see cref="Current"/> filled it. Empty when that fails.</summary>
    public required string Exe { get; init; }

    /// <summary><c>$APPIMAGE</c>, when the variable is set.</summary>
    public string? AppImage { get; init; }

    /// <summary>dpkg lists the program as part of the installed package.</summary>
    public bool DpkgOwned { get; init; }

    /// <summary>An <c>uninstall.exe</c> sits beside the program.</summary>
    public bool HasUninstaller { get; init; }

    /// <summary>The program folder holds <c>.d3skcommander-install</c>, the record <c>install.sh</c> writes.</summary>
    public bool HasInstallRecord { get; init; }

    /// <summary>The program lies below a folder holding <c>D3skCommander.sln</c> (a source build).</summary>
    public bool InSourceTree { get; init; }

    /// <summary>Facts for this process. A missing or unresolvable program file leaves <see cref="Exe"/> empty.</summary>
    public static InstallFacts Current()
    {
        var exe = CanonicalExe();
        var dir = exe.Length == 0 ? null : Path.GetDirectoryName(exe);
        return new InstallFacts
        {
            Os = OsName(),
            Exe = exe,
            AppImage = Environment.GetEnvironmentVariable("APPIMAGE"),
            DpkgOwned = exe.Length > 0 && DpkgOwns(exe),
            HasUninstaller = dir is not null && File.Exists(Path.Combine(dir, "uninstall.exe")),
            InSourceTree = exe.Length > 0 && InSourceTreeAt(exe),
            HasInstallRecord = exe.Length > 0 && HasInstallRecordAt(exe),
        };
    }

    /// <summary>v0.1: <c>current_exe().and_then(canonicalize).unwrap_or_default()</c>.</summary>
    private static string CanonicalExe()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return "";
        }

        return Canonicalize(exe) ?? "";
    }

    private static string OsName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return "other";
    }

    private static bool DpkgOwns(string exe)
    {
        const string List = "/var/lib/dpkg/info/d3skcommander.list";
        try
        {
            foreach (var line in File.ReadLines(List))
            {
                if (line == exe)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    /// <summary>An ancestor of <paramref name="exe"/> holds <c>D3skCommander.sln</c>.</summary>
    public static bool InSourceTreeAt(string exe)
    {
        for (var current = Path.GetDirectoryName(exe); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (File.Exists(Path.Combine(current, "D3skCommander.sln")))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The folder of <paramref name="exe"/> holds the <c>install.sh</c> record.</summary>
    public static bool HasInstallRecordAt(string exe)
    {
        var dir = Path.GetDirectoryName(exe);
        return dir is not null && File.Exists(Path.Combine(dir, InstallRecord.FileName));
    }

    /// <summary>Resolve the path the way Rust <c>canonicalize</c> does: absolute, links followed, or null.</summary>
    private static string? Canonicalize(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var current = root;
            var rest = full[root.Length..];
            foreach (var part in rest.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (Directory.Exists(current))
                {
                    current = ResolveLink(new DirectoryInfo(current)) ?? current;
                    continue;
                }

                if (!File.Exists(current))
                {
                    return null;
                }

                current = ResolveLink(new FileInfo(current)) ?? current;
            }

            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ResolveLink(FileSystemInfo info)
    {
        var target = info.ResolveLinkTarget(returnFinalTarget: true);
        return target?.FullName;
    }
}
