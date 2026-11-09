namespace D3skCommander.Core.Panels;

/// <summary>
/// The reserved address of the Computer view, the virtual level above every drive root, and the
/// rule that says which paths go up to it. Pure: strings in, a verdict out.
/// </summary>
public static class ComputerAddress
{
    /// <summary>The canonical text. A <see cref="Location"/> at Computer holds it as its path.</summary>
    public const string Text = "computer:";

    /// <summary>True for the address, in any case, ignoring surrounding blanks.</summary>
    public static bool IsAddress(string? text) =>
        text is not null && string.Equals(text.Trim(), Text, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the address, or the address followed by a separator and more.</summary>
    public static bool IsAddressOrUnder(string? text)
    {
        if (text is null)
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length <= Text.Length)
        {
            return IsAddress(trimmed);
        }

        return trimmed.StartsWith(Text, StringComparison.OrdinalIgnoreCase) && IsSeparator(trimmed[Text.Length]);
    }

    /// <summary>
    /// Whether going up from <paramref name="path"/> leads to Computer: it is a path root, or one of
    /// <paramref name="mountPoints"/> lying strictly under one of <paramref name="prefixes"/> (whole segments).
    /// <paramref name="windows"/> picks case-insensitive matching and drive or UNC roots.
    /// </summary>
    public static bool GoesUp(string path, IEnumerable<string> mountPoints, IEnumerable<string> prefixes, bool windows)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(mountPoints);
        ArgumentNullException.ThrowIfNull(prefixes);
        var segments = Segments(path);
        if (IsRoot(path, segments, windows))
        {
            return true;
        }

        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!mountPoints.Any(mount => SameSegments(Segments(mount), segments, comparison)))
        {
            return false;
        }

        return prefixes.Any(prefix => StrictlyUnder(segments, Segments(prefix), comparison));
    }

    private static bool IsSeparator(char c) => c == '/' || c == '\\';

    // Empty parts (doubled or trailing separators) are dropped, so "/a//b/" and "/a/b" compare equal.
    private static List<string> Segments(string path) =>
        [.. path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)];

    private static bool IsRoot(string path, List<string> segments, bool windows)
    {
        if (!windows)
        {
            return path.Length > 0 && segments.Count == 0 && path.All(c => c == '/');
        }

        if (segments.Count == 1 && segments[0].Length == 2 && segments[0][1] == ':' && char.IsAsciiLetter(segments[0][0]))
        {
            return true;
        }

        return segments.Count == 2 && path.Length > 1 && IsSeparator(path[0]) && IsSeparator(path[1]);
    }

    private static bool SameSegments(List<string> left, List<string> right, StringComparison comparison) =>
        left.Count == right.Count && StartsWith(right, left, comparison);

    private static bool StrictlyUnder(List<string> path, List<string> prefix, StringComparison comparison) =>
        prefix.Count > 0 && path.Count > prefix.Count && StartsWith(path, prefix, comparison);

    private static bool StartsWith(List<string> path, List<string> prefix, StringComparison comparison)
    {
        for (var i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(path[i], prefix[i], comparison))
            {
                return false;
            }
        }

        return true;
    }
}
