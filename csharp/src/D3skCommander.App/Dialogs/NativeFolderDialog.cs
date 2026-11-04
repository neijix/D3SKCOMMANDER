using System.Diagnostics;
using System.Runtime.InteropServices;

namespace D3skCommander.App.Dialogs;

/// <summary>Asks the operating system for one existing folder. No extra UI package.</summary>
internal static partial class NativeFolderDialog
{
    private const uint ReturnOnlyFsDirs = 0x0001;
    private const uint NewDialogStyle = 0x0040;

    public static string? Pick(string title, string? startFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        if (OperatingSystem.IsWindows())
        {
            return WindowsPick(title);
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacPick(title, cancellationToken);
        }

        return LinuxPick(title, startFolder, cancellationToken);
    }

    private static string? LinuxPick(string title, string? startFolder, CancellationToken cancellationToken)
    {
        if (ExistsOnPath("zenity"))
        {
            var args = new List<string> { "--file-selection", "--directory", "--title", title };
            if (!string.IsNullOrWhiteSpace(startFolder))
            {
                args.Add("--filename");
                args.Add(startFolder);
            }

            return Run("zenity", args, cancellationToken);
        }

        if (ExistsOnPath("kdialog"))
        {
            var args = new List<string> { "--title", title, "--getexistingdirectory", startFolder ?? "." };
            return Run("kdialog", args, cancellationToken);
        }

        throw new InvalidOperationException("A folder dialog needs zenity or kdialog.");
    }

    private static string? MacPick(string title, CancellationToken cancellationToken)
    {
        var prompt = title.Replace("\"", "", StringComparison.Ordinal);
        var script = "POSIX path of (choose folder with prompt \"" + prompt + "\")";
        return Run("osascript", ["-e", script], cancellationToken);
    }

    private static string? WindowsPick(string title)
    {
        const int chars = 260;
        var bytes = chars * sizeof(char);
        var display = Marshal.AllocHGlobal(bytes);
        var path = Marshal.AllocHGlobal(bytes);
        var titlePtr = Marshal.StringToHGlobalUni(title);
        try
        {
            var info = new Native.BrowseInfo
            {
                pszDisplayName = display,
                lpszTitle = titlePtr,
                ulFlags = ReturnOnlyFsDirs | NewDialogStyle,
            };
            return ReadChosenPath(info, path);
        }
        finally
        {
            Marshal.FreeHGlobal(display);
            Marshal.FreeHGlobal(path);
            Marshal.FreeHGlobal(titlePtr);
        }
    }

    private static string? ReadChosenPath(Native.BrowseInfo info, nint path)
    {
        var pidl = Native.SHBrowseForFolderW(ref info);
        if (pidl == 0)
        {
            return null;
        }

        try
        {
            return Native.SHGetPathFromIDListW(pidl, path) ? Marshal.PtrToStringUni(path) : null;
        }
        finally
        {
            Native.CoTaskMemFree(pidl);
        }
    }

    private static string? Run(string file, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo.FileName = file;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        if (!process.Start())
        {
            return null;
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        });
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return null;
        }

        var chosen = output.Trim();
        return chosen.Length == 0 ? null : chosen;
    }

    private static bool ExistsOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(folder, name)))
            {
                return true;
            }
        }

        return false;
    }

    private static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct BrowseInfo
        {
            public nint hwndOwner;
            public nint pidlRoot;
            public nint pszDisplayName;
            public nint lpszTitle;
            public uint ulFlags;
            public nint lpfn;
            public nint lParam;
            public int iImage;
        }

        [LibraryImport("shell32.dll")]
        internal static partial nint SHBrowseForFolderW(ref BrowseInfo lpbi);

        [LibraryImport("shell32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SHGetPathFromIDListW(nint pidl, nint pszPath);

        [LibraryImport("ole32.dll")]
        internal static partial void CoTaskMemFree(nint pv);
    }
}
