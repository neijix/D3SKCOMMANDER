using System.IO.Compression;
using System.Text;

namespace D3skCommander.Core.Tests.Archives;

/// <summary>Hand-built archives so member names stay exactly as written. SharpCompress writers rewrite some of them.</summary>
internal static class ArchiveSlip
{
    // LZMA2 7z whose only member is "../evil.txt" with body "pwned" (SharpCompress 0.50.8).
    private const string SevenZipSlip =
        "N3q8ryccAAJ5CQtxXQAAAAAAAAAmAAAAAAAAAOaHOz4BAARwd25lZAAAAIEzB64P4PQ8U6OZOvUdu0M5Gw5B7xpnUAbdCHRGsbWH4SMtfOUf3gpBzRU25i65iT5hR+4liwTMYceJ51XEBY9aUZsg2lq/EnyZO4T//+hZgAAXBgkBCVQKAdwuAf0ABwsBAAEjAwEBBV0AABAADFYKAfwcvOMAAA==";

    public static void WriteZip(string path, string name, byte[] data)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var crc = Crc32(data);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(0x04034b50);
        writer.Write((ushort)20);
        WriteEntry(writer, crc, data, nameBytes);
        writer.Write(nameBytes);
        writer.Write(data);
        var localSize = (int)stream.Position;
        writer.Write(0x02014b50);
        writer.Write((ushort)20);
        writer.Write((ushort)20);
        WriteEntry(writer, crc, data, nameBytes);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(nameBytes);
        var centralSize = (int)stream.Position - localSize;
        writer.Write(0x06054b50);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(centralSize);
        writer.Write(localSize);
        writer.Write((ushort)0);
    }

    private static void WriteEntry(BinaryWriter writer, uint crc, byte[] data, byte[] nameBytes)
    {
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(crc);
        writer.Write(data.Length);
        writer.Write(data.Length);
        writer.Write((ushort)nameBytes.Length);
        writer.Write((ushort)0);
    }

    public static void WriteTar(string path, string name, bool gzip) =>
        WriteTarMember(path, name, '0', "", "pwned"u8.ToArray(), gzip);

    public static void WriteTarLink(string path, string name, string target) =>
        WriteTarMember(path, name, '2', target, [], false);

    public static void WriteSevenZipSlip(string path) =>
        File.WriteAllBytes(path, Convert.FromBase64String(SevenZipSlip));

    private static void WriteTarMember(string path, string name, char type, string link, byte[] data, bool gzip)
    {
        var header = Header(name, link, type, data.Length);
        using var stream = File.Create(path);
        Stream dest = stream;
        GZipStream? compressed = null;
        if (gzip)
        {
            compressed = new GZipStream(stream, CompressionLevel.Fastest);
            dest = compressed;
        }

        dest.Write(header);
        if (data.Length > 0)
        {
            dest.Write(data);
            var pad = (512 - (data.Length % 512)) % 512;
            if (pad > 0)
            {
                dest.Write(new byte[pad]);
            }
        }

        dest.Write(new byte[1024]);
        compressed?.Dispose();
    }

    private static byte[] Header(string name, string link, char type, int size)
    {
        var block = new byte[512];
        Put(block, 0, name, 100);
        Put(block, 100, "0000644\0", 8);
        Put(block, 108, "0000000\0", 8);
        Put(block, 116, "0000000\0", 8);
        Put(block, 124, Convert.ToString(size, 8).PadLeft(11, '0') + "\0", 12);
        Put(block, 136, "00000000000\0", 12);
        for (var i = 148; i < 156; i++)
        {
            block[i] = (byte)' ';
        }

        block[156] = (byte)type;
        Put(block, 157, link, 100);
        Put(block, 257, "ustar\0", 6);
        Put(block, 263, "00", 2);
        long sum = 0;
        foreach (var b in block)
        {
            sum += b;
        }

        Put(block, 148, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ", 8);
        return block;
    }

    private static void Put(byte[] block, int offset, string text, int length)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        Array.Copy(bytes, 0, block, offset, Math.Min(bytes.Length, length));
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                var bit = (crc & 1) != 0;
                crc >>= 1;
                if (bit)
                {
                    crc ^= 0xEDB88320u;
                }
            }
        }

        return ~crc;
    }
}
