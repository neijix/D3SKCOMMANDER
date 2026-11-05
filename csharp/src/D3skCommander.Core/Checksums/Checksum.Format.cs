namespace D3skCommander.Core.Checksums;

public static partial class Checksum
{
    private static bool TryParseLine(string line, bool sfv, out string expected, out string relative)
    {
        expected = "";
        relative = "";
        var text = TrimEnd(line, '\r', '\n');
        if (text.Trim().Length == 0 || text.StartsWith(';') || text.StartsWith('#'))
        {
            return false;
        }

        if (sfv)
        {
            return ParseSfv(text.TrimEnd(), out expected, out relative);
        }

        return ParseDigestLine(text.TrimStart(), out expected, out relative);
    }

    private static bool ParseSfv(string line, out string expected, out string relative)
    {
        expected = "";
        relative = "";
        var space = line.LastIndexOf(' ');
        if (space < 0)
        {
            return false;
        }

        relative = line[..space].Trim();
        expected = line[(space + 1)..].Trim();
        return true;
    }

    private static bool ParseDigestLine(string line, out string expected, out string relative)
    {
        expected = "";
        relative = "";
        var space = line.IndexOf(' ');
        if (space < 0)
        {
            return false;
        }

        expected = line[..space];
        var rest = line[(space + 1)..];
        if (rest.StartsWith('*') || rest.StartsWith(' '))
        {
            rest = rest[1..];
        }

        relative = rest;
        return true;
    }

    private static IEnumerable<string> Lines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            yield return TrimEnd(text[start..i], '\r');
            start = i + 1;
        }

        if (start < text.Length)
        {
            yield return TrimEnd(text[start..], '\r');
        }
    }

    private static string TrimEnd(string text, params char[] chars)
    {
        var end = text.Length;
        while (end > 0 && Array.IndexOf(chars, text[end - 1]) >= 0)
        {
            end--;
        }

        return end == text.Length ? text : text[..end];
    }

    /// <summary>Path relative to <paramref name="baseDirectory"/>, <c>/</c>-separated. Not under it: the path itself, without a root.</summary>
    private static string DisplayRelative(string path, string baseDirectory)
    {
        var root = TrimTrailingSeparator(baseDirectory);
        string relative;
        if (path.Equals(root, StringComparison.Ordinal))
        {
            relative = "";
        }
        else
        {
            var prefix = root.Length == 0 ? "" : root + Path.DirectorySeparatorChar;
            relative = prefix.Length > 0 && path.StartsWith(prefix, StringComparison.Ordinal)
                ? path[prefix.Length..]
                : path;
        }

        return JoinComponents(relative);
    }

    private static string TrimTrailingSeparator(string path)
    {
        if (path.Length <= 1)
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string JoinComponents(string path)
    {
        var parts = new List<string>();
        foreach (var segment in path.Split(['/', '\\']))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                parts.Add("..");
                continue;
            }

            // A Windows prefix is not a path segment, matching v0.1's component filter.
            if (segment.Length == 2 && segment[1] == ':')
            {
                continue;
            }

            parts.Add(segment);
        }

        return string.Join('/', parts);
    }

    private static string Resolve(string baseDirectory, string relative)
    {
        var path = baseDirectory;
        foreach (var part in relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, part);
        }

        return path;
    }
}
