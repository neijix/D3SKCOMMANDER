using System.Text;
using System.Text.RegularExpressions;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Search;

internal enum ScanKind
{
    Found,
    NotFound,

    /// <summary>Binary (for a regex), too large, unreadable or cancelled: neither found nor "not containing".</summary>
    Skipped,
}

internal readonly record struct ScanResult(ScanKind Kind, int Line, string Text, string? Failure)
{
    public static ScanResult Skip(string? failure = null) => new(ScanKind.Skipped, 0, "", failure);
}

/// <summary>Looks for the content pattern in one file, line by line.</summary>
internal static class ContentScanner
{
    /// <summary>Files above this size are not searched for content.</summary>
    public const long MaxContentSize = 512L * 1024 * 1024;

    private const int BinaryProbe = 8 * 1024;
    private const int MaxLineChars = 300;

    public static async Task<ScanResult> ScanAsync(
        IFileSystemProvider provider,
        string path,
        ulong size,
        SearchMatchers matchers,
        CancellationToken cancellationToken)
    {
        if (size > (ulong)MaxContentSize || matchers.Content is null)
        {
            return ScanResult.Skip();
        }

        var opened = await provider.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess)
        {
            return ScanResult.Skip(opened.Error?.Message);
        }

        try
        {
            using var stream = opened.Value;
            return await ScanStreamAsync(stream, matchers.Content, matchers.ContentIsPlain, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ScanResult.Skip();
        }
        catch (IOException ex)
        {
            return ScanResult.Skip(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ScanResult.Skip(ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return ScanResult.Skip(ex.Message);
        }
    }

    private static async Task<ScanResult> ScanStreamAsync(
        Stream stream,
        Regex pattern,
        bool plain,
        CancellationToken cancellationToken)
    {
        var buffered = new BufferedStream(stream, 64 * 1024);
        if (!plain && await HasNulAsync(buffered, cancellationToken).ConfigureAwait(false))
        {
            return ScanResult.Skip();
        }

        using var reader = new StreamReader(buffered, new UTF8Encoding(false), true, 64 * 1024, leaveOpen: true);
        var number = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return new ScanResult(ScanKind.NotFound, 0, "", null);
            }

            number++;
            if (pattern.IsMatch(line))
            {
                var shown = line.Length > MaxLineChars ? line[..MaxLineChars] : line;
                return new ScanResult(ScanKind.Found, number, shown, null);
            }
        }
    }

    private static async Task<bool> HasNulAsync(BufferedStream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        var head = new byte[BinaryProbe];
        var read = await stream.ReadAtLeastAsync(head, head.Length, false, cancellationToken).ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.Begin);
        return Array.IndexOf(head, (byte)0, 0, read) >= 0;
    }
}
