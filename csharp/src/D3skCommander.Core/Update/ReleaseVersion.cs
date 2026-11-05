using System.Globalization;

namespace D3skCommander.Core.Update;

/// <summary>
/// <c>major.minor.patch[-pre]</c>, with or without a leading <c>v</c>.
/// A final release is newer than the same numbers with a pre-release suffix.
/// </summary>
public sealed record ReleaseVersion(ulong Major, ulong Minor, ulong Patch, string? Pre)
{
    /// <summary>
    /// True when <paramref name="candidate"/> is later than <paramref name="current"/>.
    /// Unreadable text is not newer.
    /// </summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        var left = Parse(candidate);
        var right = Parse(current);
        if (left is null || right is null)
        {
            return false;
        }

        return left.Compare(right) > 0;
    }

    /// <summary>Missing minor and patch are zero. A fourth number, or no number, is null.</summary>
    public static ReleaseVersion? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var core = CoreOf(text, out var pre);
        if (core is null)
        {
            return null;
        }

        var parts = core.Split('.');
        if (parts.Length == 0 || parts.Length > 3)
        {
            return null;
        }

        if (!TryPart(parts, 0, out var major))
        {
            return null;
        }

        if (!TryPart(parts, 1, out var minor))
        {
            return null;
        }

        if (!TryPart(parts, 2, out var patch))
        {
            return null;
        }

        return new ReleaseVersion(major, minor, patch, pre);
    }

    public int Compare(ReleaseVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
        {
            return minor;
        }

        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0)
        {
            return patch;
        }

        return ComparePre(Pre, other.Pre);
    }

    private static int ComparePre(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        return string.CompareOrdinal(left, right);
    }

    private static string? CoreOf(string text, out string? pre)
    {
        pre = null;
        var trimmed = text.Trim();
        var start = 0;
        while (start < trimmed.Length && (trimmed[start] == 'v' || trimmed[start] == 'V'))
        {
            start++;
        }

        trimmed = trimmed[start..];
        var plus = trimmed.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            trimmed = trimmed[..plus];
        }

        if (trimmed.Length == 0)
        {
            return null;
        }

        var dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return trimmed;
        }

        pre = trimmed[(dash + 1)..];
        return trimmed[..dash];
    }

    private static bool TryPart(string[] parts, int index, out ulong value)
    {
        value = 0;
        if (index >= parts.Length)
        {
            return true;
        }

        return ulong.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
