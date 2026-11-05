using System.Buffers.Binary;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Dbf;

/// <summary>
/// Minimal dBase III/IV/FoxPro (<c>.dbf</c>) reader for the lister: field definitions plus the
/// first records as text. Memo files are not read. The code-page byte is not interpreted;
/// text is Latin-1. Field types <c>C</c> <c>N</c> <c>F</c> <c>L</c> <c>D</c> are decoded
/// (others shown raw). A file cut short yields the records that are complete.
/// </summary>
public static class DbfReader
{
    private const int HeaderLength = 32;
    private const byte FieldTerminator = 0x0D;
    private const byte EofMark = 0x1A;

    /// <summary>Read the header and up to <paramref name="maxRecords"/> records.</summary>
    public static FileResult<DbfTable> Read(string path, int maxRecords)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRecords);

        FileStream stream;
        try
        {
            stream = File.OpenRead(path);
        }
        catch (Exception exception) when (IsFileSystem(exception))
        {
            return FileResult<DbfTable>.Fail(OperationError.FromException(exception, path));
        }

        using (stream)
        {
            try
            {
                return ReadOpened(stream, path, maxRecords);
            }
            catch (Exception exception) when (IsFileSystem(exception))
            {
                return FileResult<DbfTable>.Fail(OperationError.FromException(exception, path));
            }
        }
    }

    private static FileResult<DbfTable> ReadOpened(Stream stream, string path, int maxRecords)
    {
        var header = new byte[HeaderLength];
        if (!TryReadExact(stream, header))
        {
            return FileResult<DbfTable>.Fail(Invalid("header is truncated", path));
        }

        var totalRecords = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10));
        if (headerLength <= HeaderLength || recordLength == 0)
        {
            return FileResult<DbfTable>.Fail(Invalid("bad header or record length", path));
        }

        var descriptors = new byte[headerLength - HeaderLength];
        if (!TryReadExact(stream, descriptors))
        {
            return FileResult<DbfTable>.Fail(Invalid("field descriptors are truncated", path));
        }

        var fields = ParseFields(descriptors);
        if (fields.Count == 0)
        {
            return FileResult<DbfTable>.Fail(Invalid("no fields", path));
        }

        stream.Seek(headerLength, SeekOrigin.Begin);
        var records = new List<IReadOnlyList<string>>();
        var deleted = new List<bool>();
        ReadRecords(stream, fields, recordLength, RecordLimit(totalRecords, maxRecords), records, deleted);
        return FileResult<DbfTable>.Ok(new DbfTable(fields, records, deleted, totalRecords));
    }

    // The caller's cap is an int, so the smaller of the two always fits in one.
    private static int RecordLimit(uint totalRecords, int maxRecords) =>
        (int)Math.Min(totalRecords, (uint)maxRecords);

    private static List<DbfField> ParseFields(byte[] descriptors)
    {
        var fields = new List<DbfField>();
        for (var offset = 0; offset + HeaderLength <= descriptors.Length; offset += HeaderLength)
        {
            if (descriptors[offset] == FieldTerminator)
            {
                break;
            }

            fields.Add(ParseField(descriptors.AsSpan(offset, HeaderLength)));
        }

        return fields;
    }

    private static DbfField ParseField(ReadOnlySpan<byte> descriptor)
    {
        var nameBytes = descriptor[..11];
        var end = nameBytes.IndexOf((byte)0);
        if (end >= 0)
        {
            nameBytes = nameBytes[..end];
        }

        return new DbfField(Latin1(nameBytes).Trim(), AsciiUpper(descriptor[11]), descriptor[16], descriptor[17]);
    }

    private static void ReadRecords(
        Stream stream,
        List<DbfField> fields,
        int recordLength,
        int want,
        List<IReadOnlyList<string>> records,
        List<bool> deleted)
    {
        var buffer = new byte[recordLength];
        while (records.Count < want)
        {
            if (!TryReadExact(stream, buffer) || buffer[0] == EofMark)
            {
                return;
            }

            deleted.Add(buffer[0] == (byte)'*');
            records.Add(ParseRecord(buffer, fields));
        }
    }

    // Byte 0 is the deletion flag.
    private static List<string> ParseRecord(byte[] record, List<DbfField> fields)
    {
        var row = new List<string>(fields.Count);
        var position = 1;
        foreach (var field in fields)
        {
            var end = position + field.Length;
            var raw = Slice(record, position, end);
            row.Add(FormatValue(field.Kind, raw));
            position = end;
        }

        return row;
    }

    private static ReadOnlySpan<byte> Slice(byte[] record, int start, int end)
    {
        if (start > record.Length)
        {
            return [];
        }

        var sliceEnd = Math.Min(end, record.Length);
        return record.AsSpan(start, sliceEnd - start);
    }

    private static string FormatValue(char kind, ReadOnlySpan<byte> raw)
    {
        var text = Latin1(raw);
        if (kind == 'C')
        {
            return TrimSpaceAndNull(text, leading: false);
        }

        var trimmed = TrimSpaceAndNull(text, leading: true);
        if (kind == 'L')
        {
            return Logical(trimmed);
        }

        if (kind == 'D' && IsDate(trimmed))
        {
            return $"{trimmed[..4]}-{trimmed[4..6]}-{trimmed[6..]}";
        }

        return trimmed;
    }

    private static string Logical(string trimmed)
    {
        var upper = AsciiUpper(trimmed);
        if (upper is "T" or "Y")
        {
            return "T";
        }

        if (upper is "F" or "N")
        {
            return "F";
        }

        return string.Empty;
    }

    private static bool IsDate(string text)
    {
        if (text.Length != 8)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    // 'C' keeps leading spaces. Every other type drops spaces and NULs on both ends.
    private static string TrimSpaceAndNull(string text, bool leading)
    {
        var start = 0;
        var end = text.Length;
        if (leading)
        {
            while (start < end && IsSpaceOrNull(text[start]))
            {
                start++;
            }
        }

        while (end > start && IsSpaceOrNull(text[end - 1]))
        {
            end--;
        }

        return text[start..end];
    }

    private static bool IsSpaceOrNull(char value) => value is ' ' or '\0';

    private static string Latin1(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)bytes[i];
        }

        return new string(chars);
    }

    private static char AsciiUpper(byte value) =>
        value is >= (byte)'a' and <= (byte)'z' ? (char)(value - 32) : (char)value;

    private static string AsciiUpper(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= 'a' and <= 'z')
            {
                chars[i] = (char)(chars[i] - 32);
            }
        }

        return new string(chars);
    }

    private static bool TryReadExact(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer, read, buffer.Length - read);
            if (count == 0)
            {
                return false;
            }

            read += count;
        }

        return true;
    }

    private static OperationError Invalid(string detail, string path) =>
        OperationError.InvalidData("not a valid DBF file: " + detail, path);

    private static bool IsFileSystem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
