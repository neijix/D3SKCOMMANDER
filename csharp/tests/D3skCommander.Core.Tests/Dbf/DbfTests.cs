using System.Buffers.Binary;
using System.Text;
using D3skCommander.Core.Common;
using D3skCommander.Core.Dbf;
using D3skCommander.Core.FileSystem;
using Xunit;

namespace D3skCommander.Core.Tests.Dbf;

public class DbfTests
{
    [Fact]
    public void ParsesFieldsAndRecords()
    {
        var table = Read(Sample(), 100).Value;
        Assert.Equal(3u, table.TotalRecords);
        Assert.Equal(["NAME", "AGE", "BORN", "OK"], table.Fields.Select(field => field.Name));
        Assert.Equal('C', table.Fields[0].Kind);
        Assert.Equal(10, table.Fields[0].Length);
        Assert.Equal(["Alice", "30", "1990-01-01", "T"], table.Records[0]);
        Assert.Equal(3, table.Records.Count);
    }

    [Fact]
    public void DeletedFlagIsReported()
    {
        var table = Read(Sample(), 100).Value;
        Assert.Equal([false, true, false], table.Deleted);
        Assert.Equal("Bob", table.Records[1][0]);
    }

    [Fact]
    public void Latin1AndBlankValues()
    {
        var table = Read(Sample(), 100).Value;
        Assert.Equal(["Ren\u00e9e", "7", "", ""], table.Records[2]);
    }

    [Fact]
    public void MaxRecordsLimitsButTotalIsKept()
    {
        var table = Read(Sample(), 2).Value;
        Assert.Equal(2, table.Records.Count);
        Assert.Equal(2, table.Deleted.Count);
        Assert.Equal(3u, table.TotalRecords);
    }

    [Fact]
    public void TruncatedDataReturnsCompleteRecords()
    {
        var bytes = Sample();
        var table = Read(bytes[..^10], 100).Value;
        Assert.Equal(2, table.Records.Count);
        Assert.Equal(3u, table.TotalRecords);
    }

    [Fact]
    public void RejectsGarbage()
    {
        var truncated = Read("short"u8.ToArray(), 10);
        Assert.NotNull(truncated.Error);
        Assert.Equal(ErrorKind.InvalidData, truncated.Error.Kind);

        var zeros = Read(new byte[64], 10);
        Assert.NotNull(zeros.Error);
        Assert.Equal(ErrorKind.InvalidData, zeros.Error.Kind);
    }

    private static byte[] Descriptor(string name, byte kind, byte length, byte decimals)
    {
        var descriptor = new byte[32];
        Encoding.ASCII.GetBytes(name).CopyTo(descriptor, 0);
        descriptor[11] = kind;
        descriptor[16] = length;
        descriptor[17] = decimals;
        return descriptor;
    }

    // dBase III file: NAME C(10), AGE N(3), BORN D(8), OK L(1).
    private static byte[] Build((string Flag, byte[] Name, string Age, string Born, string Ok)[] records, uint announced)
    {
        var fields = new[]
        {
            Descriptor("NAME", (byte)'C', 10, 0),
            Descriptor("AGE", (byte)'N', 3, 0),
            Descriptor("BORN", (byte)'D', 8, 0),
            Descriptor("OK", (byte)'L', 1, 0),
        };
        var headerLength = 32 + (32 * fields.Length) + 1;
        const ushort recordLength = 1 + 10 + 3 + 8 + 1;
        var header = new byte[32];
        header[0] = 0x03;
        header[1] = 126;
        header[2] = 10;
        header[3] = 31;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), announced);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)headerLength);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), recordLength);

        using var stream = new MemoryStream();
        stream.Write(header);
        foreach (var field in fields)
        {
            stream.Write(field);
        }

        stream.WriteByte(0x0D);
        foreach (var record in records)
        {
            stream.Write(Encoding.ASCII.GetBytes(record.Flag));
            stream.Write(record.Name);
            stream.Write(Encoding.ASCII.GetBytes(record.Age));
            stream.Write(Encoding.ASCII.GetBytes(record.Born));
            stream.Write(Encoding.ASCII.GetBytes(record.Ok));
        }

        stream.WriteByte(0x1A);
        return stream.ToArray();
    }

    private static byte[] Sample()
    {
        var renee = new byte[10];
        Encoding.ASCII.GetBytes("Ren").CopyTo(renee, 0);
        renee[3] = 0xE9;
        Encoding.ASCII.GetBytes("e     ").CopyTo(renee, 4);
        return Build(
            [
                (" ", Encoding.ASCII.GetBytes("Alice     "), " 30", "19900101", "T"),
                ("*", Encoding.ASCII.GetBytes("Bob       "), " 41", "19800231", "F"),
                (" ", renee, "  7", "        ", "?"),
            ],
            3);
    }

    private static FileResult<DbfTable> Read(byte[] bytes, int max)
    {
        var directory = Directory.CreateTempSubdirectory("d3sk-dbf-");
        try
        {
            var path = Path.Combine(directory.FullName, "t.dbf");
            File.WriteAllBytes(path, bytes);
            return DbfReader.Read(path, max);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
