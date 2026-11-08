using D3skCommander.Core.Install;
using D3skCommander.Core.SettingsLocation;

namespace D3skCommander.Core.Update;

/// <summary>
/// Puts a downloaded release file in place. A source build is never replaced.
/// The Windows setup is not run here: the returned <see cref="Relaunch"/> starts it with <c>/S /R /D=&lt;install folder&gt;</c> (<c>/D=</c> last: NSIS reads it only there, unquoted).
/// </summary>
public static class UpdateApply
{
    /// <param name="version">The release version; an <c>install.sh</c> folder records it in its install record.</param>
    public static UpdateStep<Relaunch> Install(InstallKind kind, string file, IUpdateProcess? process = null, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentException.ThrowIfNullOrEmpty(file);
        if (kind is InstallKind.Source)
        {
            return UpdateStep<Relaunch>.Fail("built from source: update with git pull, then rebuild");
        }

        try
        {
            return Place(kind, file, process, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UpdateStep<Relaunch>.Fail(ex.Message);
        }
    }

    private static UpdateStep<Relaunch> Place(InstallKind kind, string file, IUpdateProcess? process, string? version)
    {
        if (kind is InstallKind.AppImage image)
        {
            FileSwap.Replace(file, image.Path);
            return UpdateStep<Relaunch>.Ok(Relaunch.Of(image.Path));
        }

        if (kind is InstallKind.Deb deb)
        {
            return InstallDeb(file, deb.Path, process);
        }

        if (kind is InstallKind.LinuxScript script)
        {
            return LinuxScriptUpdate.Apply(file, script.Folder, version);
        }

        if (kind is InstallKind.MacApp mac)
        {
            var parent = Path.GetDirectoryName(mac.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent) || !SettingsDirectory.CanWrite(parent))
            {
                return UpdateStep<Relaunch>.Fail(LinuxScriptUpdate.NotWritable(mac.Path));
            }

            MacUpdate.ReplaceBundle(file, mac.Path);
            return UpdateStep<Relaunch>.Ok(new Relaunch("open", ["-n", mac.Path]));
        }

        if (kind is InstallKind.WindowsSetup windows)
        {
            return UpdateStep<Relaunch>.Ok(new Relaunch(file, ["/S", "/R", "/D=" + InstallFolder(windows.Path)]));
        }

        if (kind is InstallKind.Portable portable)
        {
            return PortableUpdate.Install(file, portable.Path);
        }

        return UpdateStep<Relaunch>.Fail("built from source: update with git pull, then rebuild");
    }

    private static string InstallFolder(string exe) => Path.GetDirectoryName(exe) ?? "";

    private static UpdateStep<Relaunch> InstallDeb(string file, string exe, IUpdateProcess? process)
    {
        if (process is null && !OperatingSystem.IsLinux())
        {
            return UpdateStep<Relaunch>.Fail("a .deb is installed on Linux — or run: " + DebInstall.Manual(file));
        }

        var error = DebInstall.Apply(file, process ?? new UpdateProcess());
        if (error is not null)
        {
            return UpdateStep<Relaunch>.Fail(error);
        }

        return UpdateStep<Relaunch>.Ok(Relaunch.Of(exe));
    }
}
