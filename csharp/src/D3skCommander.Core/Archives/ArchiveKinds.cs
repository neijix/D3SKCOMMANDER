namespace D3skCommander.Core.Archives;

/// <summary>Extension and label rules from v0.1 <c>ArchiveKind</c>.</summary>
public static class ArchiveKinds
{
    private static readonly string[] ExternalSuffixes =
    [
        ".rar", ".arj", ".lzh", ".lha", ".cab", ".ace", ".uc2", ".gz", ".bz2", ".xz", ".iso",
    ];

    /// <summary>By file name, case-insensitive. <c>.tar.gz</c> wins over <c>.gz</c>.</summary>
    public static ArchiveKind? FromPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var lower = name.ToLowerInvariant();
        if (Ends(lower, ".tar.gz") || Ends(lower, ".tgz"))
        {
            return ArchiveKind.TarGz;
        }

        if (Ends(lower, ".zip") || Ends(lower, ".jar"))
        {
            return ArchiveKind.Zip;
        }

        if (Ends(lower, ".tar"))
        {
            return ArchiveKind.Tar;
        }

        if (Ends(lower, ".7z"))
        {
            return ArchiveKind.SevenZ;
        }

        if (IsExternal(lower))
        {
            return ArchiveKind.External;
        }

        return null;
    }

    /// <summary>The kinds <see cref="ArchiveIo.Create"/> can write, in v0.1's order.</summary>
    public static ArchiveKind[] Creatable() =>
    [
        ArchiveKind.Zip,
        ArchiveKind.Tar,
        ArchiveKind.TarGz,
        ArchiveKind.SevenZ,
    ];

    /// <summary>File extension without the dot. Empty for <see cref="ArchiveKind.External"/>.</summary>
    public static string Extension(ArchiveKind kind)
    {
        if (kind == ArchiveKind.Zip)
        {
            return "zip";
        }

        if (kind == ArchiveKind.Tar)
        {
            return "tar";
        }

        if (kind == ArchiveKind.TarGz)
        {
            return "tar.gz";
        }

        if (kind == ArchiveKind.SevenZ)
        {
            return "7z";
        }

        if (kind == ArchiveKind.External)
        {
            return "";
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown archive kind.");
    }

    public static string Label(ArchiveKind kind)
    {
        if (kind == ArchiveKind.Zip)
        {
            return "ZIP";
        }

        if (kind == ArchiveKind.Tar)
        {
            return "TAR";
        }

        if (kind == ArchiveKind.TarGz)
        {
            return "TAR.GZ";
        }

        if (kind == ArchiveKind.SevenZ)
        {
            return "7-Zip";
        }

        if (kind == ArchiveKind.External)
        {
            return "RAR / other (external tool)";
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown archive kind.");
    }

    private static bool IsExternal(string lowerName)
    {
        foreach (var suffix in ExternalSuffixes)
        {
            if (Ends(lowerName, suffix))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Ends(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.Ordinal);
}
