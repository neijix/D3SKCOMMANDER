namespace D3skCommander.Core.Checksums;

/// <summary>CRC32 (SFV), MD5, SHA-1 and SHA-256, in v0.1's order.</summary>
public enum ChecksumAlgo
{
    Crc32,
    Md5,
    Sha1,
    Sha256,
}

/// <summary>Extension and label of a <see cref="ChecksumAlgo"/>, as v0.1 <c>Algo</c>.</summary>
public static class ChecksumAlgos
{
    public static IReadOnlyList<ChecksumAlgo> All { get; } =
    [
        ChecksumAlgo.Crc32,
        ChecksumAlgo.Md5,
        ChecksumAlgo.Sha1,
        ChecksumAlgo.Sha256,
    ];

    /// <summary>List extension: <c>sfv</c>, <c>md5</c>, <c>sha</c>, <c>sha256</c>.</summary>
    public static string Extension(this ChecksumAlgo algo) => algo switch
    {
        ChecksumAlgo.Crc32 => "sfv",
        ChecksumAlgo.Md5 => "md5",
        ChecksumAlgo.Sha1 => "sha",
        ChecksumAlgo.Sha256 => "sha256",
        _ => throw new ArgumentOutOfRangeException(nameof(algo), algo, "No extension for this algorithm."),
    };

    public static string Label(this ChecksumAlgo algo) => algo switch
    {
        ChecksumAlgo.Crc32 => "CRC32",
        ChecksumAlgo.Md5 => "MD5",
        ChecksumAlgo.Sha1 => "SHA-1",
        ChecksumAlgo.Sha256 => "SHA-256",
        _ => throw new ArgumentOutOfRangeException(nameof(algo), algo, "No label for this algorithm."),
    };

    /// <summary>
    /// Algorithm named by a list extension. <c>.sha1</c> is SHA-1, same as <c>.sha</c>.
    /// The dot is optional.
    /// </summary>
    public static ChecksumAlgo? FromExtension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var bare = extension.Length > 0 && extension[0] == '.' ? extension[1..] : extension;
        if (bare.Equals("sfv", StringComparison.OrdinalIgnoreCase))
        {
            return ChecksumAlgo.Crc32;
        }

        if (bare.Equals("md5", StringComparison.OrdinalIgnoreCase))
        {
            return ChecksumAlgo.Md5;
        }

        if (bare.Equals("sha", StringComparison.OrdinalIgnoreCase) || bare.Equals("sha1", StringComparison.OrdinalIgnoreCase))
        {
            return ChecksumAlgo.Sha1;
        }

        if (bare.Equals("sha256", StringComparison.OrdinalIgnoreCase))
        {
            return ChecksumAlgo.Sha256;
        }

        return null;
    }
}
