using D3skCommander.Core.Index;
using Xunit;
using static D3skCommander.Core.Tests.Index.IndexTree;

namespace D3skCommander.Core.Tests.Index;

public sealed class NameIndexFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "d3sk-idx-file-" + Guid.NewGuid().ToString("N"));

    public NameIndexFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File => NameIndexFile.PathIn(_folder);

    [Fact]
    public void SaveThenLoadGivesTheSameIndex()
    {
        var index = Small();

        Assert.True(NameIndexFile.Save(index, File));
        var loaded = NameIndexFile.Load(File);

        Assert.NotNull(loaded);
        Assert.Equal(index.Count, loaded.Count);
        Assert.Equal(index.BuiltAt, loaded.BuiltAt);
        for (var i = 0; i < index.Count; i++)
        {
            Assert.Equal(index.PathOf(i), loaded.PathOf(i));
            Assert.Equal(index.SizeOf(i), loaded.SizeOf(i));
            Assert.Equal(index.ModifiedOf(i), loaded.ModifiedOf(i));
            Assert.Equal(index.IsFolder(i), loaded.IsFolder(i));
        }

        Assert.Equal(Names(Run(index, "report")), Names(Run(loaded, "report")));
        Assert.Equal(index.BuiltAt, NameIndexFile.ReadBuiltAt(File));
    }

    [Fact]
    public void SavingLeavesNoTempFileAndReplacesAnOlderIndex()
    {
        Assert.True(NameIndexFile.Save(NameIndex.Empty, File));
        Assert.True(NameIndexFile.Save(Small(), File));

        Assert.Equal([NameIndexFile.FileName], Directory.GetFiles(Path.GetDirectoryName(File)!).Select(static path => Path.GetFileName(path)).ToArray());
        Assert.Equal(Small().Count, NameIndexFile.Load(File)!.Count);
    }

    [Fact]
    public void AnEmptyIndexRoundTrips()
    {
        Assert.True(NameIndexFile.Save(NameIndex.Empty, File));

        Assert.Equal(0, NameIndexFile.Load(File)!.Count);
    }

    [Fact]
    public void AMissingFileIsNoIndex()
    {
        Assert.Null(NameIndexFile.Load(File));
        Assert.Null(NameIndexFile.ReadBuiltAt(File));
    }

    [Fact]
    public void AnUnknownVersionIsNoIndex()
    {
        NameIndexFile.Save(Small(), File);
        var bytes = System.IO.File.ReadAllBytes(File);
        bytes[4] = 99;
        System.IO.File.WriteAllBytes(File, bytes);

        Assert.Null(NameIndexFile.Load(File));
        Assert.Null(NameIndexFile.ReadBuiltAt(File));
    }

    [Fact]
    public void ATruncatedFileIsNoIndex()
    {
        NameIndexFile.Save(Small(), File);
        var bytes = System.IO.File.ReadAllBytes(File);
        System.IO.File.WriteAllBytes(File, bytes[..(bytes.Length / 2)]);

        Assert.Null(NameIndexFile.Load(File));
    }

    [Fact]
    public void GarbageAfterAGoodHeaderIsNoIndex()
    {
        NameIndexFile.Save(Small(), File);
        var bytes = System.IO.File.ReadAllBytes(File);
        new Random(7).NextBytes(bytes.AsSpan(16));
        System.IO.File.WriteAllBytes(File, bytes);

        Assert.Null(NameIndexFile.Load(File));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(15)]
    public void AShortOrEmptyFileIsNoIndex(int length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllBytes(File, new byte[length]);

        Assert.Null(NameIndexFile.Load(File));
    }

    [Fact]
    public void ABrokenParentLinkInAnOtherwiseWellFormedFileIsNoIndex()
    {
        // A well-formed container whose parent array points forward: the consistency check refuses it.
        var index = Small();
        NameIndexFile.Save(index, File);
        var forged = Forge(File, parentOfSecond: 5);

        Assert.Null(NameIndexFile.Load(forged));
    }

    private string Forge(string source, int parentOfSecond)
    {
        var target = Path.Combine(_folder, "forged.bin");
        using var input = System.IO.File.OpenRead(source);
        var header = new byte[16];
        input.ReadExactly(header);
        using var packed = new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var body = new MemoryStream();
        packed.CopyTo(body);
        var raw = body.ToArray();
        var count = BitConverter.ToInt32(raw, 0);
        var pool = BitConverter.ToInt32(raw, 4);
        var parentsAt = 8 + (pool * 2) + ((count + 1) * 4);
        BitConverter.GetBytes(parentOfSecond).CopyTo(raw, parentsAt + 4);
        using var output = System.IO.File.Create(target);
        output.Write(header);
        using var repacked = new System.IO.Compression.BrotliStream(output, System.IO.Compression.CompressionLevel.Fastest);
        repacked.Write(raw);
        repacked.Flush();
        return target;
    }
}
