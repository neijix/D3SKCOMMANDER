using System.Buffers;
using System.Globalization;
using System.IO.Hashing;
using D3skCommander.Core.Common;

namespace D3skCommander.Core.Splitter;

/// <summary>Part paths, the byte copy, and the <c>.crc</c> sidecar. v0.1 <c>splitter.rs</c>.</summary>
internal static class SplitParts
{
    private const int BufferSize = 1 << 20;

    public static string PartPath(string directory, string name, int number) =>
        Path.Combine(directory, $"{name}.{number:000}");

    public static string SourceDirectory(string anyPart)
    {
        var directory = Path.GetDirectoryName(anyPart);
        return string.IsNullOrEmpty(directory) ? "." : directory;
    }

    public static bool TryBaseName(string anyPart, out string baseName, out OperationError? error)
    {
        var file = Path.GetFileName(anyPart);
        if (string.IsNullOrEmpty(file))
        {
            baseName = "";
            error = OperationError.InvalidData("not a file name", anyPart);
            return false;
        }

        var dot = file.LastIndexOf('.');
        if (dot <= 0 || !IsPartExtension(file[(dot + 1)..]))
        {
            baseName = "";
            error = OperationError.InvalidData($"{file}: not a split part (.001) or .crc file", anyPart);
            return false;
        }

        baseName = file[..dot];
        error = null;
        return true;
    }

    public static OperationError? Copy(
        Stream source,
        Stream destination,
        ulong limit,
        Crc32 crc,
        ref ulong done,
        ulong total,
        Func<ulong, ulong, bool> progress,
        out ulong written)
    {
        written = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            return CopyBuffer(source, destination, limit, crc, ref done, total, progress, buffer, ref written);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static bool? CheckCrc(string path, uint crc, ulong size)
    {
        if (!TryReadCrc(path, out var recordedCrc, out var recordedSize))
        {
            return null;
        }

        var sizeOk = recordedSize is null || recordedSize.Value == size;
        return recordedCrc == crc && sizeOk;
    }

    public static void RemoveAll(List<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A part that never landed, or one already gone, is fine.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as v0.1: cleanup is best-effort.
            }
        }
    }

    public static bool IsFileSystem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static OperationError? CopyBuffer(
        Stream source,
        Stream destination,
        ulong limit,
        Crc32 crc,
        ref ulong done,
        ulong total,
        Func<ulong, ulong, bool> progress,
        byte[] buffer,
        ref ulong written)
    {
        while (written < limit)
        {
            var count = ReadChunk(source, buffer, limit - written, out var error);
            if (error is not null)
            {
                return error;
            }

            if (count == 0)
            {
                break;
            }

            try
            {
                destination.Write(buffer, 0, count);
            }
            catch (Exception exception) when (IsFileSystem(exception))
            {
                return OperationError.FromException(exception);
            }

            crc.Append(buffer.AsSpan(0, count));
            written += (ulong)count;
            done += (ulong)count;
            if (!progress(done, total))
            {
                return OperationError.Cancelled();
            }
        }

        return null;
    }

    private static int ReadChunk(Stream source, byte[] buffer, ulong remaining, out OperationError? error)
    {
        var want = remaining > (ulong)BufferSize ? BufferSize : (int)remaining;
        try
        {
            error = null;
            return source.Read(buffer, 0, want);
        }
        catch (Exception exception) when (IsFileSystem(exception))
        {
            error = OperationError.FromException(exception);
            return 0;
        }
    }

    private static bool IsPartExtension(string extension)
    {
        if (extension.Length == 0)
        {
            return false;
        }

        if (extension.Equals("crc", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var character in extension)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadCrc(string path, out uint crc, out ulong? size)
    {
        crc = 0;
        size = null;
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (IsFileSystem(exception))
        {
            return false;
        }

        uint? parsedCrc = null;
        foreach (var raw in text.Split('\n'))
        {
            ReadCrcLine(raw.TrimEnd('\r'), ref parsedCrc, ref size);
        }

        if (parsedCrc is null)
        {
            return false;
        }

        crc = parsedCrc.Value;
        return true;
    }

    private static void ReadCrcLine(string line, ref uint? crc, ref ulong? size)
    {
        var split = line.IndexOf('=');
        if (split < 0)
        {
            return;
        }

        var key = line[..split].Trim().ToLowerInvariant();
        var value = line[(split + 1)..].Trim();
        if (key == "size" && ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
        {
            size = parsedSize;
        }
        else if (key == "crc32" && TryHex(value, out var parsedCrc))
        {
            crc = parsedCrc;
        }
    }

    private static bool TryHex(string text, out uint value)
    {
        value = 0;
        if (text.Length is 0 or > 8)
        {
            return false;
        }

        foreach (var character in text)
        {
            var digit = character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'f' => character - 'a' + 10,
                >= 'A' and <= 'F' => character - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
            {
                return false;
            }

            value = (value << 4) | (uint)digit;
        }

        return true;
    }
}
