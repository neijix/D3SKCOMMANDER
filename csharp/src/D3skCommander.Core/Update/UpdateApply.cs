using D3skCommander.Core.Install;

namespace D3skCommander.Core.Update;

/// <summary>
/// Puts a downloaded release file in place. A source build is never replaced.
/// The Windows setup is not run here: the returned <see cref="Relaunch"/> starts it with <c>/S /R</c>.
/// </summary>
public static class UpdateApply
{
    public static UpdateStep<Relaunch> Install(InstallKind kind, string file, IUpdateProcess? process = null)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentException.ThrowIfNullOrEmpty(file);
        if (kind is InstallKind.Source)
        {
            return UpdateStep<Relaunch>.Fail("built from source: update with git pull, then cargo build");
        }

        try
        {
            return Place(kind, file, process);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UpdateStep<Relaunch>.Fail(ex.Message);
        }
    }

    private static UpdateStep<Relaunch> Place(InstallKind kind, string file, IUpdateProcess? process)
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

        if (kind is InstallKind.MacApp mac)
        {
            MacUpdate.ReplaceBundle(file, mac.Path);
            return UpdateStep<Relaunch>.Ok(new Relaunch("open", ["-n", mac.Path]));
        }

        if (kind is InstallKind.WindowsSetup)
        {
            return UpdateStep<Relaunch>.Ok(new Relaunch(file, ["/S", "/R"]));
        }

        if (kind is InstallKind.Portable portable)
        {
            return PortableUpdate.Install(file, portable.Path);
        }

        return UpdateStep<Relaunch>.Fail("built from source: update with git pull, then cargo build");
    }

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
