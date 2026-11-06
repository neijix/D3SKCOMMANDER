using System.Buffers.Binary;

namespace D3skCommander.Core.Viewer;

/// <summary>
/// The EXIF orientation of a photo (1 = as stored, 2-8 = the flips and quarter turns a camera
/// records instead of turning the pixels) and the turn that shows it upright. Read from JPEG
/// (APP1), PNG (eXIf) and WebP (EXIF chunk); TIFF carries the same tag, which
/// <see cref="TiffImage"/> applies itself. Never throws: anything unreadable is 1.
/// </summary>
public static class PictureOrientation
{
    public const int Upright = 1;

    private const ushort OrientationTag = 0x0112;
    private const int MaxSegments = 512;
    private const int MaxExifBytes = 1024 * 1024;

    /// <summary>The orientation recorded in the photo at <paramref name="path"/>, 1 when none.</summary>
    public static int Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[PictureSignature.Length];
            var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            var exif = PictureSignature.Of(head.AsSpan(0, read)) switch
            {
                PictureFormat.Jpeg => FindJpegExif(stream),
                PictureFormat.Png => FindChunk(stream, 8, "eXIf"u8.ToArray(), bigEndianSize: true, sizeFirst: true),
                PictureFormat.WebP => FindChunk(stream, PictureSignature.Length, "EXIF"u8.ToArray(), bigEndianSize: false, sizeFirst: false),
                _ => null,
            };
            return exif is null ? Upright : FromTiff(exif);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or EndOfStreamException)
        {
            return Upright;
        }
    }

    /// <summary>The orientation tag of the first directory of a TIFF-structured EXIF block, 1 when absent.</summary>
    public static int FromTiff(ReadOnlySpan<byte> tiff)
    {
        if (tiff.StartsWith("Exif\0\0"u8))
        {
            tiff = tiff[6..];
        }

        if (tiff.Length < 8)
        {
            return Upright;
        }

        var little = tiff.StartsWith("II"u8);
        if (!little && !tiff.StartsWith("MM"u8))
        {
            return Upright;
        }

        var directory = ReadUInt32(tiff[4..], little);
        if (directory > (uint)(tiff.Length - 2))
        {
            return Upright;
        }

        var entries = tiff[(int)directory..];
        var count = ReadUInt16(entries, little);
        for (var i = 0; i < count; i++)
        {
            var start = 2 + (i * 12);
            if (start + 12 > entries.Length)
            {
                break;
            }

            var entry = entries.Slice(start, 12);
            if (ReadUInt16(entry, little) == OrientationTag)
            {
                var value = ReadUInt16(entry[8..], little);
                return value is >= 1 and <= 8 ? value : Upright;
            }
        }

        return Upright;
    }

    /// <summary>
    /// <paramref name="source"/> turned upright for <paramref name="orientation"/>: a quarter turn
    /// (5-8) swaps width and height. Pixels are moved as four-byte units, whatever their channel order.
    /// </summary>
    public static RasterImage Apply(RasterImage source, int orientation)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (orientation is < 2 or > 8)
        {
            return source;
        }

        int w = source.Width, h = source.Height;
        var turned = orientation >= 5;
        var outWidth = turned ? h : w;
        var pixels = new byte[source.Rgba.Length];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (dx, dy) = orientation switch
                {
                    2 => (w - 1 - x, y),
                    3 => (w - 1 - x, h - 1 - y),
                    4 => (x, h - 1 - y),
                    5 => (y, x),
                    6 => (h - 1 - y, x),
                    7 => (h - 1 - y, w - 1 - x),
                    _ => (y, w - 1 - x),
                };
                Buffer.BlockCopy(source.Rgba, ((y * w) + x) * 4, pixels, ((dy * outWidth) + dx) * 4, 4);
            }
        }

        return new RasterImage(outWidth, turned ? w : h, pixels);
    }

    // JPEG: markers after SOI, each FFxx + big-endian length; EXIF is an APP1 starting "Exif\0\0".
    private static byte[]? FindJpegExif(Stream stream)
    {
        stream.Position = 2;
        Span<byte> marker = stackalloc byte[4];
        for (var i = 0; i < MaxSegments; i++)
        {
            if (stream.ReadAtLeast(marker, 4, throwOnEndOfStream: false) < 4 || marker[0] != 0xFF)
            {
                return null;
            }

            var kind = marker[1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(marker[2..]) - 2;
            if (kind is 0xDA or 0xD9 || length < 0)
            {
                return null;
            }

            if (kind == 0xE1 && length is >= 14 and <= MaxExifBytes)
            {
                var block = ReadBlock(stream, length);
                if (block?.AsSpan().StartsWith("Exif\0\0"u8) == true)
                {
                    return block;
                }

                continue;
            }

            stream.Seek(length, SeekOrigin.Current);
        }

        return null;
    }

    // PNG chunks: size (BE) then type, data, CRC. WebP (RIFF) chunks: type then size (LE), data padded to even.
    private static byte[]? FindChunk(Stream stream, long start, byte[] wanted, bool bigEndianSize, bool sizeFirst)
    {
        stream.Position = start;
        Span<byte> header = stackalloc byte[8];
        for (var i = 0; i < MaxSegments; i++)
        {
            if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8)
            {
                return null;
            }

            var sizeBytes = sizeFirst ? header[..4] : header[4..];
            var type = sizeFirst ? header[4..] : header[..4];
            var size = bigEndianSize ? BinaryPrimitives.ReadUInt32BigEndian(sizeBytes) : BinaryPrimitives.ReadUInt32LittleEndian(sizeBytes);
            if (type.SequenceEqual(wanted))
            {
                return size <= MaxExifBytes ? ReadBlock(stream, (int)size) : null;
            }

            var skip = (long)size + (sizeFirst ? 4 : size % 2);
            if (stream.Position + skip > stream.Length)
            {
                return null;
            }

            stream.Seek(skip, SeekOrigin.Current);
        }

        return null;
    }

    private static byte[]? ReadBlock(Stream stream, int length)
    {
        var block = new byte[length];
        return stream.ReadAtLeast(block, length, throwOnEndOfStream: false) == length ? block : null;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool little) =>
        bytes.Length < 2 ? (ushort)0 : little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
}
