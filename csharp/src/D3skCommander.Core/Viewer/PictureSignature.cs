namespace D3skCommander.Core.Viewer;

/// <summary>A picture container recognised from its first bytes, whatever the file is called.</summary>
public enum PictureFormat
{
    Unknown,
    Png,
    Jpeg,
    Gif,
    WebP,
    Tiff,
}

/// <summary>
/// Recognises the common photo containers from their magic bytes, so a picture with a wrong or
/// missing extension still opens as one. BMP and ICO are left out: their two-byte marks also
/// start ordinary files.
/// </summary>
public static class PictureSignature
{
    /// <summary>Bytes <see cref="Of(ReadOnlySpan{byte})"/> needs to recognise every format.</summary>
    public const int Length = 12;

    public static PictureFormat Of(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return PictureFormat.Png;
        }

        if (head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return PictureFormat.Jpeg;
        }

        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8))
        {
            return PictureFormat.Gif;
        }

        if (head.Length >= Length && head.StartsWith("RIFF"u8) && head[8..Length].SequenceEqual("WEBP"u8))
        {
            return PictureFormat.WebP;
        }

        if (head.StartsWith("II*\0"u8) || head.StartsWith("MM\0*"u8))
        {
            return PictureFormat.Tiff;
        }

        return PictureFormat.Unknown;
    }

    /// <summary>The format of the file at <paramref name="path"/>; Unknown when it cannot be read.</summary>
    public static PictureFormat Of(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> head = stackalloc byte[Length];
            var read = stream.ReadAtLeast(head, Length, throwOnEndOfStream: false);
            return Of(head[..read]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return PictureFormat.Unknown;
        }
    }
}
