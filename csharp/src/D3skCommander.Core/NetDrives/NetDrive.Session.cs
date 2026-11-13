using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.NetDrives;

public static partial class NetDrive
{
    private const string NoGio =
        "gio not found: install GVfs (package gvfs / gvfs-backends) to mount network drives, "
        + "or mount the share yourself and open its folder";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The OS command <see cref="Connect"/> runs. The password is in <see cref="MountCommand.StandardInput"/>
    /// on Linux and macOS, and in neither the program nor the arguments. Windows names the WNet API and
    /// carries no command line.
    /// </summary>
    public static MountCommand MountCommandFor(Share share, Credentials credentials)
    {
        ArgumentNullException.ThrowIfNull(share);
        ArgumentNullException.ThrowIfNull(credentials);
        if (OperatingSystem.IsWindows())
        {
            return new MountCommand("WNetAddConnection2", [], "");
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MountCommand("osascript", ["-"], MacMountScript(share, credentials));
        }

        if (OperatingSystem.IsLinux())
        {
            return new MountCommand("gio", ["mount", share.Uri()], GioAnswers(credentials));
        }

        return new MountCommand("", [], "");
    }

    /// <summary>
    /// Mounts <paramref name="share"/> and gives the local folder to browse (the typed sub-folder included).
    /// <paramref name="letter"/> (<c>Z:</c>) maps a drive letter on Windows; elsewhere it is ignored.
    /// Blocks until the OS answers.
    /// </summary>
    public static FileResult<string> Connect(Share share, Credentials credentials, string? letter)
    {
        ArgumentNullException.ThrowIfNull(share);
        ArgumentNullException.ThrowIfNull(credentials);
        var mounted = MountRoot(share, credentials, letter);
        if (!mounted.IsSuccess)
        {
            return mounted;
        }

        return FileResult<string>.Ok(share.Inside(mounted.Value));
    }

    /// <summary>
    /// Asks Windows Credential Manager to keep the login for <paramref name="server"/>, so Windows logs in by itself
    /// next time (a favorite on <c>\\server\</c> after a restart). Only on Windows and with a user name.
    /// </summary>
    public static FileResult<string> Remember(string server, Credentials credentials)
    {
        ArgumentException.ThrowIfNullOrEmpty(server);
        ArgumentNullException.ThrowIfNull(credentials);
        if (!OperatingSystem.IsWindows())
        {
            return Fail<string>("saving a login needs Windows");
        }

        var user = WindowsUser(credentials);
        if (user is null)
        {
            return Fail<string>("a user name is needed to save a login");
        }

        return WindowsCredentials.Save(server, user, credentials.Password);
    }

    /// <summary><c>domain\user</c>, <c>user</c>, or null without a user name.</summary>
    internal static string? WindowsUser(Credentials credentials)
    {
        if (credentials.User.Length == 0)
        {
            return null;
        }

        return credentials.Domain.Length == 0 ? credentials.User : credentials.Domain + "\\" + credentials.User;
    }

    /// <summary>Unmounts the network drive <paramref name="path"/> is on and gives what was disconnected.</summary>
    public static FileResult<string> Disconnect(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (OperatingSystem.IsWindows())
        {
            return WindowsNet.Disconnect(path);
        }

        if (OperatingSystem.IsMacOS())
        {
            return DisconnectMac(path);
        }

        if (OperatingSystem.IsLinux())
        {
            return DisconnectLinux(path);
        }

        return Fail<string>("network drives are not supported on this system");
    }

    /// <summary>Shares the OS currently has mounted. A missing GVFS folder or a failed listing is empty.</summary>
    public static IReadOnlyList<ConnectedShare> ListConnected()
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsNet.List();
        }

        if (OperatingSystem.IsMacOS())
        {
            var listing = Run("mount", [], "");
            return listing.IsSuccess ? ConnectedFromMountOutput(listing.Value) : [];
        }

        if (OperatingSystem.IsLinux())
        {
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(runtime))
            {
                return [];
            }

            return ConnectedFromGvfs(Path.Combine(runtime, "gvfs"));
        }

        return [];
    }

    private static FileResult<string> MountRoot(Share share, Credentials credentials, string? letter)
    {
        if (OperatingSystem.IsWindows())
        {
            return MountWindows(share, credentials, letter);
        }

        if (share.Name.Length == 0 && share.Scheme is "smb" or "afp")
        {
            return Fail<string>("missing share name: \\\\server\\share");
        }

        if (OperatingSystem.IsMacOS())
        {
            return MountMac(share, credentials);
        }

        if (OperatingSystem.IsLinux())
        {
            return MountLinux(share, credentials);
        }

        return Fail<string>("network drives are not supported on this system");
    }

    private static FileResult<string> MountLinux(Share share, Credentials credentials)
    {
        var command = MountCommandFor(share, credentials);
        var mounted = Gio(command.Arguments, command.StandardInput);
        if (!mounted.IsSuccess && mounted.Error!.Message == NoGio)
        {
            return mounted;
        }

        var info = Gio(["info", share.Uri()], "");
        if (info.IsSuccess)
        {
            var local = GioLocalPath(info.Value);
            if (local is not null)
            {
                return FileResult<string>.Ok(local);
            }
        }

        if (!mounted.IsSuccess)
        {
            return mounted;
        }

        return Fail<string>(share.Uri() + " is mounted but has no local folder (is gvfsd-fuse running?)");
    }

    private static FileResult<string> MountMac(Share share, Credentials credentials)
    {
        if (share.Scheme is not ("smb" or "afp" or "nfs" or "ftp" or "dav" or "davs"))
        {
            return Fail<string>(share.Scheme + ":// is not supported by macOS");
        }

        var command = MountCommandFor(share, credentials);
        var mounted = Run(command.Program, command.Arguments, command.StandardInput);
        if (!mounted.IsSuccess)
        {
            return mounted;
        }

        var listing = Run("mount", [], "");
        if (!listing.IsSuccess)
        {
            return listing;
        }

        var point = MacMountPoint(listing.Value, share);
        if (point is null)
        {
            return Fail<string>(share.Uri() + " was mounted but its folder was not found");
        }

        return FileResult<string>.Ok(point);
    }

    [SupportedOSPlatform("windows")]
    private static FileResult<string> MountWindows(Share share, Credentials credentials, string? letter)
    {
        if (share.Scheme != "smb")
        {
            return Fail<string>(share.Scheme + ":// is not supported on Windows: use \\\\server\\share");
        }

        return WindowsNet.Connect(share, credentials, letter);
    }

    private static FileResult<string> DisconnectLinux(string path)
    {
        var uri = GvfsUri(path);
        if (uri is null)
        {
            return Fail<string>(path + " is not on a network drive");
        }

        var unmounted = Gio(["mount", "-u", uri], "");
        if (!unmounted.IsSuccess)
        {
            return unmounted;
        }

        return FileResult<string>.Ok(uri);
    }

    private static FileResult<string> DisconnectMac(string path)
    {
        var listing = Run("mount", [], "");
        if (!listing.IsSuccess)
        {
            return listing;
        }

        var point = MacMountContaining(listing.Value, path);
        if (point is null)
        {
            return Fail<string>(path + " is not on a network drive");
        }

        var unmounted = Run("diskutil", ["unmount", point], "");
        if (!unmounted.IsSuccess)
        {
            return unmounted;
        }

        return FileResult<string>.Ok(point);
    }

    private static FileResult<string> Gio(IReadOnlyList<string> arguments, string input)
    {
        var ran = Run("gio", arguments, input);
        if (!ran.IsSuccess && ran.Error!.Message == "gio not found")
        {
            return Fail<string>(NoGio);
        }

        return ran;
    }

    private static FileResult<string> Run(string program, IReadOnlyList<string> arguments, string input)
    {
        Process? process = null;
        try
        {
            process = Process.Start(StartInfo(program, arguments));
        }
        catch (Exception exception) when (IsStartFailure(exception))
        {
            var message = IsMissing(exception) ? program + " not found" : program + ": " + exception.Message;
            return Fail<string>(message);
        }

        if (process is null)
        {
            return Fail<string>(program + " not found");
        }

        using (process)
        {
            return Finish(process, program, input);
        }
    }

    private static ProcessStartInfo StartInfo(string program, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = program,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    private static FileResult<string> Finish(Process process, string program, string input)
    {
        try
        {
            process.StandardInput.Write(input);
        }
        catch (IOException)
        {
            // A tool that exits before reading everything is not an error here.
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode == 0)
        {
            return FileResult<string>.Ok(stdout);
        }

        var detail = stderr.Trim();
        var message = detail.Length > 0 ? detail : stdout.Trim();
        if (message.Length == 0)
        {
            message = program + " failed (exit status: " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ")";
        }

        return Fail<string>(message);
    }

    private static bool IsStartFailure(Exception exception) =>
        exception is Win32Exception or FileNotFoundException or IOException or PlatformNotSupportedException;

    private static bool IsMissing(Exception exception) =>
        exception is FileNotFoundException || exception is Win32Exception win32 && win32.NativeErrorCode is 2 or 3;
}
