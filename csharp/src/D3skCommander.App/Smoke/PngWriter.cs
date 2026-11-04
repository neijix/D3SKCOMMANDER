using System.IO.Compression;

namespace D3skCommander.App.Smoke;

/// <summary>Uncompressed-filter RGBA PNG. Used when the platform encoder writes no file.</summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void Write(string path, int width, int height, ReadOnlySpan<byte> pixels, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        using var stream = File.Create(path);
        stream.Write(Signature);
        WriteChunk(stream, "IHDR"u8, Header(width, height));
        WriteChunk(stream, "IDAT"u8, Compress(Rows(width, height, pixels, stride)));
        WriteChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static byte[] Header(int width, int height)
    {
        var data = new byte[13];
        WriteBe(data, (uint)width);
        WriteBe(data.AsSpan(4), (uint)height);
        data[8] = 8;
        data[9] = 6;
        return data;
    }

    private static byte[] Rows(int width, int height, ReadOnlySpan<byte> pixels, int stride)
    {
        var rowBytes = width * 4;
        var raw = new byte[height * (rowBytes + 1)];
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            raw[offset] = 0;
            offset++;
            pixels.Slice(y * stride, rowBytes).CopyTo(raw.AsSpan(offset, rowBytes));
            offset += rowBytes;
        }

        return raw;
    }

    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[4];
        WriteBe(header, (uint)data.Length);
        stream.Write(header);
        stream.Write(type);
        stream.Write(data);
        var crc = Update(Update(0xFFFFFFFFu, type), data);
        WriteBe(header, ~crc);
        stream.Write(header);
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xEDB88320u & mask);
            }
        }

        return crc;
    }

    private static void WriteBe(Span<byte> target, uint value)
    {
        target[0] = (byte)(value >> 24);
        target[1] = (byte)(value >> 16);
        target[2] = (byte)(value >> 8);
        target[3] = (byte)value;
    }
}
