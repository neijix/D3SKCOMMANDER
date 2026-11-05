namespace D3skCommander.Core.Install;

/// <summary>Decides <see cref="InstallKind"/> from <see cref="InstallFacts"/>, in v0.1's order.</summary>
public static class InstallDetection
{
    /// <summary>
    /// Source checkout first, then AppImage, macOS bundle, <c>.deb</c>, Windows setup, else portable.
    /// </summary>
    public static InstallKind Detect(InstallFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.InCargoTarget)
        {
            return new InstallKind.Source();
        }

        if (facts.Os == "linux" && facts.AppImage is not null)
        {
            return new InstallKind.AppImage(facts.AppImage);
        }

        if (facts.Os == "macos")
        {
            var bundle = MacBundle(facts.Exe);
            if (bundle is not null)
            {
                return new InstallKind.MacApp(bundle);
            }
        }

        if (facts.Os == "linux" && facts.DpkgOwned)
        {
            return new InstallKind.Deb(facts.Exe);
        }

        if (facts.Os == "windows" && facts.HasUninstaller)
        {
            return new InstallKind.WindowsSetup(facts.Exe);
        }

        return new InstallKind.Portable(facts.Exe);
    }

    /// <summary>The nearest ancestor whose extension is <c>.app</c>, including <paramref name="exe"/>.</summary>
    private static string? MacBundle(string exe)
    {
        for (var current = exe; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (string.Equals(Path.GetExtension(current), ".app", StringComparison.Ordinal))
            {
                return current;
            }
        }

        return null;
    }
}
