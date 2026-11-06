using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace D3skCommander.Core.Index;

/// <summary>
/// The saved index: a 16-byte header (magic, version, when it was built) then the arrays, Brotli-compressed.
/// A file of another version, a truncated or a corrupt one loads as "no index"; it never throws.
/// </summary>
public static class NameIndexFile
{
    public const int Version = 1;

    /// <summary>Folder under the settings folder, and the file in it.</summary>
    public const string FolderName = "index";

    public const string FileName = "names.bin";

    private const int HeaderLength = 16;
    private const int MaxEntries = 100_000_000;
    private const int MaxNameLength = 256;
    private static readonly byte[] Magic = "D3NI"u8.ToArray();

    public static string PathIn(string settingsFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(settingsFolder);
        return Path.Combine(settingsFolder, FolderName, FileName);
    }

    /// <summary>Writes the index next to <paramref name="path"/> and renames it into place. False when the disk refuses.</summary>
    public static bool Save(NameIndex index, string path)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                WriteHeader(file, index.BuiltAt);
                using var packed = new BrotliStream(file, CompressionLevel.Fastest);
                WriteBody(packed, index);
            }

            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return false;
        }
    }

    /// <summary>The index in the file, or null when there is none or it cannot be trusted.</summary>
    public static NameIndex? Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            if (ReadHeader(file) is not { } builtAt)
            {
                return null;
            }

            using var packed = new BrotliStream(file, CompressionMode.Decompress);
            return ReadBody(packed, builtAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>When the saved index was built, from the header alone; null when there is no readable header.</summary>
    public static DateTimeOffset? ReadBuiltAt(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ReadHeader(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteHeader(Stream stream, DateTimeOffset builtAt)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], Version);
        BinaryPrimitives.WriteInt64LittleEndian(header[8..], builtAt.ToUnixTimeSeconds());
        stream.Write(header);
    }

    private static DateTimeOffset? ReadHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        if (stream.Read(header) != HeaderLength || !header[..4].SequenceEqual(Magic)
            || BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != Version)
        {
            return null;
        }

        var seconds = BinaryPrimitives.ReadInt64LittleEndian(header[8..]);
        return seconds is < 0 or > 253_402_300_799 ? null : DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private static void WriteBody(Stream stream, NameIndex index)
    {
        Span<byte> counts = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(counts, index.Count);
        BinaryPrimitives.WriteInt32LittleEndian(counts[4..], index.Pool.Length);
        stream.Write(counts);
        stream.Write(MemoryMarshal.AsBytes(index.Pool));
        stream.Write(MemoryMarshal.AsBytes(index.Starts));
        stream.Write(MemoryMarshal.AsBytes(index.Parents));
        stream.Write(MemoryMarshal.AsBytes(index.Sizes));
        stream.Write(MemoryMarshal.AsBytes(index.Times));
        stream.Write(index.Flags);
    }

    private static NameIndex? ReadBody(Stream stream, DateTimeOffset builtAt)
    {
        if (!BitConverter.IsLittleEndian)
        {
            return null;
        }

        Span<byte> counts = stackalloc byte[8];
        stream.ReadExactly(counts);
        var count = BinaryPrimitives.ReadInt32LittleEndian(counts);
        var poolLength = BinaryPrimitives.ReadInt32LittleEndian(counts[4..]);
        if (count is < 0 or > MaxEntries || poolLength < 0 || poolLength > (long)count * MaxNameLength)
        {
            return null;
        }

        var pool = new char[poolLength];
        var start = new int[count + 1];
        var parent = new int[count];
        var size = new long[count];
        var time = new uint[count];
        var flags = new byte[count];
        stream.ReadExactly(MemoryMarshal.AsBytes(pool.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(start.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(parent.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(size.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(time.AsSpan()));
        stream.ReadExactly(flags);
        return NameIndex.Create(pool, start, parent, size, time, flags, builtAt);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp file left behind is overwritten by the next save.
        }
    }
}
