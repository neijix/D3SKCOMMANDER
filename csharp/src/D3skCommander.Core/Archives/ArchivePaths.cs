namespace D3skCommander.Core.Archives;

/// <summary>Member-name rules shared by every format: separators, zip-slip, implied folders.</summary>
public static class ArchivePaths
{
    public static string Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var parts = Split(raw);
        return string.Join('/', parts);
    }

    /// <summary>Why <paramref name="raw"/> must never be written under the destination, or null when it may.</summary>
    public static string? UnsafeReason(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Contains('\0', StringComparison.Ordinal))
        {
            return "NUL byte";
        }

        if (raw.Length > 0 && (raw[0] == '/' || raw[0] == '\\'))
        {
            return "absolute path";
        }

        foreach (var part in Split(raw))
        {
            if (part == "..")
            {
                return "parent directory component";
            }

            if (part.Length >= 2 && char.IsAsciiLetter(part[0]) && part[1] == ':')
            {
                return "drive prefix";
            }
        }

        return null;
    }

    internal static List<ArchiveEntry> WithImpliedDirectories(List<ArchiveEntry> entries)
    {
        var explicitDirs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                explicitDirs.Add(entry.Path);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var output = new List<ArchiveEntry>(entries.Count);
        foreach (var entry in entries)
        {
            AddParents(entry.Path, explicitDirs, seen, output);
            if (entry.IsDirectory && !seen.Add(entry.Path))
            {
                continue;
            }

            output.Add(entry);
        }

        return output;
    }

    private static void AddParents(
        string path,
        HashSet<string> explicitDirs,
        HashSet<string> seen,
        List<ArchiveEntry> output)
    {
        var comps = path.Split('/');
        var prefix = "";
        for (var i = 0; i < comps.Length - 1; i++)
        {
            prefix = prefix.Length == 0 ? comps[i] : prefix + "/" + comps[i];
            if (!explicitDirs.Contains(prefix) && seen.Add(prefix))
            {
                output.Add(new ArchiveEntry(prefix, 0, null, true, null));
            }
        }
    }

    private static List<string> Split(string raw)
    {
        var parts = raw.Split(['/', '\\']);
        var kept = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            kept.Add(part);
        }

        return kept;
    }
}
