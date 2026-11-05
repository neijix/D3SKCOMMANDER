using System.Security.Cryptography;

namespace D3skCommander.Core.Update;

/// <summary>
/// SHA-256 of a release file. The asset <c>digest</c> wins; otherwise a <c>SHA256SUMS</c>
/// line (<c>hex  name</c> or <c>hex *name</c>) supplies it.
/// </summary>
public static class ReleaseChecksum
{
    public static string? Expected(ReleaseAsset asset, string? sums)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!string.IsNullOrEmpty(asset.Sha256))
        {
            return asset.Sha256.ToLowerInvariant();
        }

        if (string.IsNullOrEmpty(sums))
        {
            return null;
        }

        return LineFor(sums, asset.Name);
    }

    /// <summary>Lowercase SHA-256 of <paramref name="content"/>.</summary>
    public static string Of(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>Lowercase hex of an already computed digest.</summary>
    public static string Format(byte[] digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>The lowercase hex for <paramref name="fileName"/>, or null when the list has no such line.</summary>
    public static string? LineFor(string sums, string fileName)
    {
        ArgumentNullException.ThrowIfNull(sums);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        var offset = 0;
        while (offset < sums.Length)
        {
            var end = sums.IndexOf('\n', offset);
            var line = end < 0 ? sums[offset..] : sums[offset..end];
            offset = end < 0 ? sums.Length : end + 1;
            var hex = HexFor(line, fileName);
            if (hex is not null)
            {
                return hex;
            }
        }

        return null;
    }

    private static string? HexFor(string line, string fileName)
    {
        if (line.EndsWith('\r'))
        {
            line = line[..^1];
        }

        if (line.Length == 0 || line[0] == '#')
        {
            return null;
        }

        var space = line.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0)
        {
            return null;
        }

        var hex = line[..space].ToLowerInvariant();
        if (!IsSha256(hex))
        {
            return null;
        }

        var name = line[(space + 1)..].TrimStart(' ');
        if (name.StartsWith('*'))
        {
            name = name[1..];
        }

        if (!string.Equals(name, fileName, StringComparison.Ordinal))
        {
            return null;
        }

        return hex;
    }

    private static bool IsSha256(string hex)
    {
        if (hex.Length != 64)
        {
            return false;
        }

        foreach (var c in hex)
        {
            if (!IsHex(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHex(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return true;
        }

        return c >= 'a' && c <= 'f';
    }
}
