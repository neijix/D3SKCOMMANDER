namespace D3skCommander.Core.Install;

/// <summary>
/// How this copy of the program was installed. The update check (a later row) reuses this type
/// to choose a release file; it does not define a second kind.
/// </summary>
public abstract record InstallKind
{
    private InstallKind()
    {
    }

    /// <summary>A Linux AppImage (<c>$APPIMAGE</c>). <paramref name="Path"/> is that file.</summary>
    public sealed record AppImage(string Path) : InstallKind;

    /// <summary>Installed from the <c>.deb</c>. <paramref name="Path"/> is the program file.</summary>
    public sealed record Deb(string Path) : InstallKind;

    /// <summary>A macOS <c>.app</c> bundle. <paramref name="Path"/> is the bundle.</summary>
    public sealed record MacApp(string Path) : InstallKind;

    /// <summary>Installed by the Windows setup. <paramref name="Path"/> is the program file.</summary>
    public sealed record WindowsSetup(string Path) : InstallKind;

    /// <summary>
    /// Installed by <c>install.sh</c>: <paramref name="Folder"/> holds the app and its <c>.d3skcommander-install</c> record.
    /// </summary>
    public sealed record LinuxScript(string Folder) : InstallKind;

    /// <summary>A program file copied by hand. <paramref name="Path"/> is that file.</summary>
    public sealed record Portable(string Path) : InstallKind;

    /// <summary>Built from a source checkout (the solution sits above the program). Nothing on disk is replaced.</summary>
    public sealed record Source : InstallKind;
}
