using System.IO.Hashing;
using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Splitter;
using Xunit;

namespace D3skCommander.Core.Tests.Splitter;

/// <summary>P17: the ten tests of v0.1 <c>src/core/splitter.rs</c>, plus the job wrapper.</summary>
public class SplitterTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Fact]
    public void SplitMakesPartsAndCrcFile()
    {
        using var sample = new SampleFile(2500);
        var outDir = sample.Folder("parts");
        var split = FileSplitter.Split(sample.Source, outDir, 1000, Yes);
        var parts = split.Value;
        Assert.Equal(3, parts.Count);
        Assert.Equal(Path.Combine(outDir, "data.bin.001"), parts[0]);
        Assert.Equal(1000, new FileInfo(parts[0]).Length);
        Assert.Equal(500, new FileInfo(parts[2]).Length);
        var crc = File.ReadAllText(Path.Combine(outDir, "data.bin.crc"));
        var expected = $"filename=data.bin\nsize=2500\ncrc32={Crc32.HashToUInt32(sample.Data):X8}\n";
        Assert.Equal(expected, crc);
    }

    [Fact]
    public void ExactMultipleHasNoEmptyTrailingPart()
    {
        using var sample = new SampleFile(2000);
        var split = FileSplitter.Split(sample.Source, sample.Folder("p"), 1000, Yes);
        Assert.Equal(2, split.Value.Count);
    }

    [Fact]
    public void RoundTripWithCrcOk()
    {
        using var sample = new SampleFile(5000);
        var partsDir = sample.Folder("parts");
        var parts = FileSplitter.Split(sample.Source, partsDir, 1024, Yes).Value;
        Assert.Equal(5, parts.Count);
        var report = FileSplitter.Combine(parts[2], sample.Folder("joined"), Yes).Value;
        Assert.Equal(5, report.Parts);
        Assert.Equal(true, report.CrcOk);
        Assert.Equal(sample.Data, File.ReadAllBytes(report.Output));

        var again = FileSplitter.Combine(Path.Combine(partsDir, "data.bin.crc"), sample.Folder("j2"), Yes).Value;
        Assert.Equal(true, again.CrcOk);
    }

    [Fact]
    public void CorruptedPartIsDetected()
    {
        using var sample = new SampleFile(3000);
        var partsDir = sample.Folder("parts");
        var parts = FileSplitter.Split(sample.Source, partsDir, 1000, Yes).Value;
        var bytes = File.ReadAllBytes(parts[1]);
        bytes[10] ^= 0xFF;
        File.WriteAllBytes(parts[1], bytes);
        var report = FileSplitter.Combine(parts[0], sample.Folder("out"), Yes).Value;
        Assert.Equal(false, report.CrcOk);
    }

    [Fact]
    public void MissingCrcFileGivesNone()
    {
        using var sample = new SampleFile(1500);
        var partsDir = sample.Folder("parts");
        var parts = FileSplitter.Split(sample.Source, partsDir, 1000, Yes).Value;
        File.Delete(Path.Combine(partsDir, "data.bin.crc"));
        var report = FileSplitter.Combine(parts[0], sample.Folder("out"), Yes).Value;
        Assert.Null(report.CrcOk);
    }

    [Fact]
    public void EmptyFileRoundTrip()
    {
        using var sample = new SampleFile(0);
        var parts = FileSplitter.Split(sample.Source, sample.Folder("p"), 100, Yes).Value;
        Assert.Single(parts);
        var report = FileSplitter.Combine(parts[0], sample.Folder("o"), Yes).Value;
        Assert.Equal(true, report.CrcOk);
        Assert.Equal(0, new FileInfo(report.Output).Length);
    }

    [Fact]
    public void CancelRemovesParts()
    {
        using var sample = new SampleFile(5000);
        var outDir = sample.Folder("p");
        var split = FileSplitter.Split(sample.Source, outDir, 1000, (done, _) => done < 2000);
        Assert.Equal(ErrorKind.Cancelled, split.Error!.Kind);
        Assert.False(File.Exists(Path.Combine(outDir, "data.bin.001")));
    }

    [Fact]
    public void BadInputs()
    {
        using var sample = new SampleFile(10);
        Assert.False(FileSplitter.Split(sample.Source, sample.Root, 0, Yes).IsSuccess);
        var notAPart = FileSplitter.Combine(sample.Source, sample.Root, Yes);
        Assert.Equal(ErrorKind.InvalidData, notAPart.Error!.Kind);
        var missing = FileSplitter.Combine(Path.Combine(sample.Root, "nope.001"), sample.Folder("o"), Yes);
        Assert.Equal(ErrorKind.NotFound, missing.Error!.Kind);
    }

    [Fact]
    public void ParseSizeForms()
    {
        Assert.Equal(512UL, FileSplitter.ParseSize("512").Bytes);
        Assert.Equal(102_400UL, FileSplitter.ParseSize("100k").Bytes);
        Assert.Equal(700UL * 1024 * 1024, FileSplitter.ParseSize("700MB").Bytes);
        Assert.Equal(4UL * 1024 * 1024 * 1024, FileSplitter.ParseSize("4G").Bytes);
        Assert.Equal(1_509_949UL, FileSplitter.ParseSize("1.44M").Bytes);
        Assert.Equal(2048UL, FileSplitter.ParseSize(" 2 kb ").Bytes);
        Assert.Equal(1536UL, FileSplitter.ParseSize("1,5k").Bytes);
    }

    [Fact]
    public void ParseSizeRejectsGarbage()
    {
        foreach (var bad in new[] { "", "k", "12x", "-5", "1.2.3", "99999999999999999999T" })
        {
            Assert.False(FileSplitter.ParseSize(bad).IsSuccess, bad);
        }
    }

    [Fact]
    public async Task SplitAndCombineRunAsJobs()
    {
        using var sample = new SampleFile(5000);
        var token = TestContext.Current.CancellationToken;
        var service = new SplitterService(new JobService());
        var split = service.Split(sample.Source, sample.Folder("parts"), 1024, token);
        var splitResult = await split.Job.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, splitResult.Outcome);
        Assert.Empty(splitResult.Failed);
        Assert.Equal(5, split.Parts.Count);

        var combine = service.Combine(split.Parts[2], sample.Folder("joined"), token);
        var combineResult = await combine.Job.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, combineResult.Outcome);
        Assert.Empty(combineResult.Failed);
        Assert.NotNull(combine.Report);
        Assert.Equal(true, combine.Report.CrcOk);
        Assert.Equal(sample.Data, await File.ReadAllBytesAsync(combine.Report.Output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledJobLeavesNoPart()
    {
        using var sample = new SampleFile(5000);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var split = new SplitterService(new JobService()).Split(sample.Source, sample.Folder("p"), 1000, cancel.Token);
        var result = await split.Job.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Cancelled, result.Outcome);
        Assert.False(File.Exists(Path.Combine(sample.Folder("p"), "data.bin.001")));
    }

    [Fact]
    public async Task CrcMismatchFailsTheCombineJob()
    {
        using var sample = new SampleFile(3000);
        var partsDir = sample.Folder("parts");
        var parts = FileSplitter.Split(sample.Source, partsDir, 1000, Yes).Value;
        var bytes = File.ReadAllBytes(parts[1]);
        bytes[10] ^= 0xFF;
        File.WriteAllBytes(parts[1], bytes);

        var combine = new SplitterService(new JobService()).Combine(
            parts[0], sample.Folder("out"), TestContext.Current.CancellationToken);
        var result = await combine.Job.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        var failed = Assert.Single(result.Failed);
        Assert.Equal(ErrorKind.InvalidData, failed.Error.Kind);
        Assert.Contains("CRC mismatch", failed.Error.Message, StringComparison.Ordinal);
        Assert.NotNull(combine.Report);
        Assert.Equal(false, combine.Report.CrcOk);
        Assert.True(File.Exists(combine.Report.Output));
    }

    private static bool Yes(ulong done, ulong total) => true;

    private static byte[] Sample(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)(i * 7 % 251);
        }

        return data;
    }

    private sealed class SampleFile : IDisposable
    {
        public SampleFile(int length)
        {
            Root = Directory.CreateTempSubdirectory("d3sk-split").FullName;
            Data = Sample(length);
            Source = Path.Combine(Root, "data.bin");
            File.WriteAllBytes(Source, Data);
        }

        public string Root { get; }

        public string Source { get; }

        public byte[] Data { get; }

        public string Folder(string name) => Path.Combine(Root, name);

        public void Dispose() => Directory.Delete(Root, true);
    }
}
