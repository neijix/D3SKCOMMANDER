using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

namespace D3skCommander.Core.Checksums;

/// <summary>
/// Checksums of files and <c>.sfv</c> / <c>.md5</c> / <c>.sha</c> / <c>.sha256</c> lists.
/// CRC32 is uppercase 8-digit hex; the others are lowercase hex. Cancel throws
/// <see cref="OperationCanceledException"/> with the message <c>cancelled</c>.
/// </summary>
public static partial class Checksum
{
    private const int BufferSize = 1 << 20;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Hex digest of <paramref name="path"/>. CRC32 is uppercase.</summary>
    public static string HashFile(string path, ChecksumAlgo algo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return HashStream(stream, algo, cancellationToken, null);
    }

    /// <summary>
    /// Write a list for <paramref name="files"/>. SFV is <c>path HEX</c>; the others are
    /// <c>hex *path</c>. Paths are relative to <paramref name="baseDirectory"/> and use <c>/</c>.
    /// Returns the number of lines. The file is created only after every file hashes.
    /// </summary>
    public static int WriteChecksumFile(
        IReadOnlyList<string> files,
        string baseDirectory,
        ChecksumAlgo algo,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        WriteChecksumFile(files, baseDirectory, algo, outputPath, cancellationToken, null);

    /// <summary>
    /// Check every file of a list. Paths are relative to the list's folder.
    /// The algorithm comes from the extension, else from each hex length.
    /// </summary>
    public static IReadOnlyList<ChecksumVerdict> VerifyChecksumFile(string listPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(listPath);
        var text = Utf8.GetString(File.ReadAllBytes(listPath));
        var algo = ChecksumAlgos.FromExtension(Path.GetExtension(listPath));
        var baseDirectory = Path.GetDirectoryName(listPath) ?? ".";
        var sfv = algo == ChecksumAlgo.Crc32;
        var results = new List<ChecksumVerdict>();
        foreach (var line in Lines(text))
        {
            if (!TryParseLine(line, sfv, out var expected, out var relative))
            {
                continue;
            }

            var verdict = VerifyEntry(baseDirectory, expected, relative, algo, cancellationToken);
            ThrowIfCancelled(cancellationToken);
            results.Add(verdict);
        }

        return results;
    }

    /// <summary>
    /// v0.1 <c>checksum_out</c>: <c>name.ext</c> for one file, <c>CHECKSUM.ext</c> otherwise,
    /// under <paramref name="baseDirectory"/>. Empty when <paramref name="oncePerFile"/> is set.
    /// </summary>
    public static string OutputPath(
        IReadOnlyList<string> files,
        string baseDirectory,
        ChecksumAlgo algo,
        bool oncePerFile)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(baseDirectory);
        if (oncePerFile)
        {
            return "";
        }

        var name = files.Count == 1
            ? OutputFileName(files[0], algo)
            : "CHECKSUM." + algo.Extension();
        return Path.Combine(baseDirectory, name);
    }

    internal static int WriteChecksumFile(
        IReadOnlyList<string> files,
        string baseDirectory,
        ChecksumAlgo algo,
        string outputPath,
        CancellationToken cancellationToken,
        Action<int>? onBytes)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        var text = new StringBuilder();
        foreach (var file in files)
        {
            ArgumentException.ThrowIfNullOrEmpty(file);
            var digest = DigestOf(file, algo, cancellationToken, onBytes);
            AppendLine(text, algo, DisplayRelative(file, baseDirectory), digest);
        }

        File.WriteAllText(outputPath, text.ToString(), Utf8);
        return files.Count;
    }

    private static string OutputFileName(string path, ChecksumAlgo algo)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Path.GetFileName(path) + "." + algo.Extension();
    }

    private static void AppendLine(StringBuilder text, ChecksumAlgo algo, string relative, string digest)
    {
        if (algo == ChecksumAlgo.Crc32)
        {
            text.Append(relative).Append(' ').Append(digest).Append('\n');
            return;
        }

        text.Append(digest).Append(" *").Append(relative).Append('\n');
    }

    private static string DigestOf(string file, ChecksumAlgo algo, CancellationToken cancellationToken, Action<int>? onBytes)
    {
        try
        {
            using var stream = File.OpenRead(file);
            return HashStream(stream, algo, cancellationToken, onBytes);
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException($"{file}: cancelled");
        }
        catch (Exception ex)
        {
            throw new IOException($"{file}: {ex.Message}", ex);
        }
    }

    private static string HashStream(Stream stream, ChecksumAlgo algo, CancellationToken cancellationToken, Action<int>? onBytes)
    {
        var buffer = new byte[BufferSize];
        if (algo == ChecksumAlgo.Crc32)
        {
            var crc = new Crc32();
            ReadAll(stream, buffer, cancellationToken, onBytes, chunk => crc.Append(chunk));
            return crc.GetCurrentHashAsUInt32().ToString("X8");
        }

        using var hash = IncrementalHash.CreateHash(AlgorithmName(algo));
        ReadAll(stream, buffer, cancellationToken, onBytes, chunk => hash.AppendData(chunk));
        return Convert.ToHexStringLower(hash.GetCurrentHash());
    }

    private static void ReadAll(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken,
        Action<int>? onBytes,
        AppendChunk append)
    {
        while (true)
        {
            ThrowIfCancelled(cancellationToken);
            var read = stream.Read(buffer);
            if (read == 0)
            {
                return;
            }

            append(buffer.AsSpan(0, read));
            onBytes?.Invoke(read);
        }
    }

    private delegate void AppendChunk(ReadOnlySpan<byte> chunk);

    private static HashAlgorithmName AlgorithmName(ChecksumAlgo algo) => algo switch
    {
        ChecksumAlgo.Md5 => HashAlgorithmName.MD5,
        ChecksumAlgo.Sha1 => HashAlgorithmName.SHA1,
        ChecksumAlgo.Sha256 => HashAlgorithmName.SHA256,
        _ => throw new ArgumentOutOfRangeException(nameof(algo), algo, "No digest for this algorithm."),
    };

    private static void ThrowIfCancelled(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("cancelled");
        }
    }

    private static ChecksumVerdict VerifyEntry(
        string baseDirectory,
        string expected,
        string relative,
        ChecksumAlgo? algo,
        CancellationToken cancellationToken)
    {
        var chosen = algo ?? FromHexLength(expected);
        if (chosen is null)
        {
            return new ChecksumVerdict(relative, ChecksumVerdictKind.Error, "cannot tell the checksum algorithm");
        }

        var path = Resolve(baseDirectory, relative);
        if (!File.Exists(path))
        {
            return new ChecksumVerdict(relative, ChecksumVerdictKind.Missing);
        }

        try
        {
            var got = HashFile(path, chosen.Value, cancellationToken);
            if (got.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                return new ChecksumVerdict(relative, ChecksumVerdictKind.Ok);
            }

            return new ChecksumVerdict(relative, ChecksumVerdictKind.Mismatch);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ChecksumVerdict(relative, ChecksumVerdictKind.Error, ex.Message);
        }
    }

    private static ChecksumAlgo? FromHexLength(string hex) => hex.Length switch
    {
        8 => ChecksumAlgo.Crc32,
        32 => ChecksumAlgo.Md5,
        40 => ChecksumAlgo.Sha1,
        64 => ChecksumAlgo.Sha256,
        _ => null,
    };
}
