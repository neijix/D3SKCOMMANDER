using System.Globalization;

namespace D3skCommander.Core.Archives;

/// <summary>Replaces a known archive extension when the pack format changes (v0.1 <c>with_archive_ext</c>).</summary>
public static class PackRules
{
    private static readonly string[] Known = [".tar.gz", ".tgz", ".zip", ".tar", ".7z"];

    public static string WithArchiveExtension(string path, ArchiveKind kind)
    {
        ArgumentNullException.ThrowIfNull(path);
        var lower = path.ToLowerInvariant();
        var stem = path;
        foreach (var extension in Known)
        {
            if (lower.EndsWith(extension, StringComparison.Ordinal))
            {
                stem = path[..^extension.Length];
                break;
            }
        }

        return stem + "." + ArchiveKinds.Extension(kind);
    }
}

/// <summary>Dated backup ZIP name: <c>{base}-backup-{yyyyMMdd-HHmmss}.zip</c>.</summary>
public static class BackupZip
{
    public static string BaseName(IReadOnlyList<string> sources, string? activeDirectory)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 1)
        {
            var name = Path.GetFileName(sources[0]);
            return string.IsNullOrEmpty(name) ? "backup" : name;
        }

        if (!string.IsNullOrEmpty(activeDirectory))
        {
            var trimmed = activeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return "backup";
    }

    /// <param name="localStamp">The local clock, as v0.1's <c>chrono::Local::now()</c>. Its own components are formatted.</param>
    public static string FileName(string baseName, DateTimeOffset localStamp)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseName);
        var stamp = localStamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return baseName + "-backup-" + stamp + ".zip";
    }
}
