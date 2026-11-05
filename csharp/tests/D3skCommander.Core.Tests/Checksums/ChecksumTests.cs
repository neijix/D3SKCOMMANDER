using D3skCommander.Core.Checksums;
using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using Xunit;

namespace D3skCommander.Core.Tests.Checksums;

/// <summary>P16: the nine tests of <c>src/core/checksum.rs</c>, the output-name rule, and create/verify as jobs.</summary>
public class ChecksumTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Fact]
    public void KnownVectorsForAbc()
    {
        Assert.Equal("352441C2", HashOf("abc", ChecksumAlgo.Crc32));
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", HashOf("abc", ChecksumAlgo.Md5));
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", HashOf("abc", ChecksumAlgo.Sha1));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", HashOf("abc", ChecksumAlgo.Sha256));
    }

    [Fact]
    public void EmptyFileVectors()
    {
        Assert.Equal("00000000", HashOf("", ChecksumAlgo.Crc32));
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", HashOf("", ChecksumAlgo.Md5));
    }

    [Fact]
    public void CancelInterrupts()
    {
        using var bench = new Bench();
        var path = bench.PathOf("f");
        File.WriteAllText(path, "abc");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var error = Assert.Throws<OperationCanceledException>(() => Checksum.HashFile(path, ChecksumAlgo.Md5, cancel.Token));
        Assert.Equal("cancelled", error.Message);
    }

    [Fact]
    public void AlgoMetadata()
    {
        Assert.Equal(4, ChecksumAlgos.All.Count);
        Assert.Equal("sfv", ChecksumAlgo.Crc32.Extension());
        Assert.Equal("sha", ChecksumAlgo.Sha1.Extension());
        Assert.Equal("SHA-256", ChecksumAlgo.Sha256.Label());
    }

    [Fact]
    public void WriteFormats()
    {
        using var bench = new Bench();
        var files = bench.Sample();
        var md5 = bench.PathOf("x.md5");
        var count = Checksum.WriteChecksumFile(files, bench.Root, ChecksumAlgo.Md5, md5, Stop);
        Assert.Equal(2, count);
        var text = File.ReadAllText(md5);
        Assert.Contains("900150983cd24fb0d6963f7d28e17f72 *a.txt\n", text, StringComparison.Ordinal);
        Assert.Contains(" *sub/b.txt\n", text, StringComparison.Ordinal);
        var sfv = bench.PathOf("x.sfv");
        Checksum.WriteChecksumFile(files, bench.Root, ChecksumAlgo.Crc32, sfv, Stop);
        Assert.StartsWith("a.txt 352441C2\n", File.ReadAllText(sfv), StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyRoundTripAllAlgos()
    {
        foreach (var algo in ChecksumAlgos.All)
        {
            using var bench = new Bench();
            var files = bench.Sample();
            var list = bench.PathOf("list." + algo.Extension());
            Checksum.WriteChecksumFile(files, bench.Root, algo, list, Stop);
            var results = Checksum.VerifyChecksumFile(list, Stop);
            Assert.Equal(2, results.Count);
            Assert.All(results, row => Assert.Equal(ChecksumVerdictKind.Ok, row.Kind));
        }
    }

    [Fact]
    public void VerifyDetectsMismatchAndMissing()
    {
        using var bench = new Bench();
        var files = bench.Sample();
        var list = bench.PathOf("list.sha256");
        Checksum.WriteChecksumFile(files, bench.Root, ChecksumAlgo.Sha256, list, Stop);
        File.WriteAllText(files[0], "changed");
        File.Delete(files[1]);
        var results = Checksum.VerifyChecksumFile(list, Stop);
        Assert.Equal(new ChecksumVerdict("a.txt", ChecksumVerdictKind.Mismatch), results[0]);
        Assert.Equal(new ChecksumVerdict("sub/b.txt", ChecksumVerdictKind.Missing), results[1]);
    }

    [Fact]
    public void VerifyGuessesAlgoFromHexLength()
    {
        using var bench = new Bench();
        File.WriteAllText(bench.PathOf("a.txt"), "abc");
        var list = bench.PathOf("sums.txt");
        File.WriteAllText(list, "# comment\n900150983cd24fb0d6963f7d28e17f72  a.txt\r\nnothex *a.txt\n");
        var results = Checksum.VerifyChecksumFile(list, Stop);
        Assert.Equal(ChecksumVerdictKind.Ok, results[0].Kind);
        Assert.Equal(ChecksumVerdictKind.Error, results[1].Kind);
        Assert.Equal("cannot tell the checksum algorithm", results[1].Detail);
    }

    [Fact]
    public void VerifySfvSkipsCommentsAndSpacesInNames()
    {
        using var bench = new Bench();
        File.WriteAllText(bench.PathOf("my file.txt"), "abc");
        var list = bench.PathOf("l.sfv");
        File.WriteAllText(list, "; made by test\nmy file.txt 352441c2\n");
        var results = Checksum.VerifyChecksumFile(list, Stop);
        Assert.Equal([new ChecksumVerdict("my file.txt", ChecksumVerdictKind.Ok)], results);
    }

    [Fact]
    public void OutputNameRule()
    {
        Assert.Equal(
            Path.Combine("/data", "notes.txt.sha256"),
            Checksum.OutputPath(["/data/notes.txt"], "/data", ChecksumAlgo.Sha256, false));
        Assert.Equal(
            Path.Combine("/data", "a.txt.sfv"),
            Checksum.OutputPath(["/data/a.txt"], "/data", ChecksumAlgo.Crc32, false));
        Assert.Equal(
            Path.Combine("/data", "a.txt.sha"),
            Checksum.OutputPath(["/data/a.txt"], "/data", ChecksumAlgo.Sha1, false));
        Assert.Equal(
            Path.Combine("/data", "CHECKSUM.md5"),
            Checksum.OutputPath(["/data/a.txt", "/data/b.txt"], "/data", ChecksumAlgo.Md5, false));
        Assert.Equal(
            Path.Combine("/data", "CHECKSUM.sha256"),
            Checksum.OutputPath([], "/data", ChecksumAlgo.Sha256, false));
        Assert.Equal("", Checksum.OutputPath(["/data/a.txt", "/data/b.txt"], "/data", ChecksumAlgo.Sha256, true));
    }

    [Fact]
    public async Task CreateAndVerifyRunAsJobs()
    {
        using var bench = new Bench();
        var files = bench.Sample();
        var output = Checksum.OutputPath(files, bench.Root, ChecksumAlgo.Sha256, false);
        var service = new ChecksumService(new JobService());
        var created = service.Create(files, bench.Root, ChecksumAlgo.Sha256, false, " " + output + " ");
        Assert.Equal(ChecksumService.CreateTitle, created.Job.Title);
        var createResult = await Finish(created.Job);
        Assert.Equal(JobOutcome.Completed, createResult.Outcome);
        Assert.Empty(createResult.Failed);
        Assert.Equal(2, created.LinesWritten);
        Assert.True(File.Exists(output));
        var text = File.ReadAllText(output);
        Assert.Contains("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad *a.txt\n", text, StringComparison.Ordinal);
        Assert.Contains(" *sub/b.txt\n", text, StringComparison.Ordinal);

        var verified = service.Verify([output]);
        Assert.Equal(ChecksumService.VerifyTitle, verified.Job.Title);
        var verifyResult = await Finish(verified.Job);
        Assert.Equal(JobOutcome.Completed, verifyResult.Outcome);
        Assert.Empty(verifyResult.Failed);
        Assert.Equal(2, verified.Verdicts.Count);
        Assert.All(verified.Verdicts, row => Assert.Equal(ChecksumVerdictKind.Ok, row.Kind));
    }

    [Fact]
    public async Task VerifyJobReportsMismatchAndMissing()
    {
        using var bench = new Bench();
        var files = bench.Sample();
        var output = bench.PathOf("list.sha256");
        var service = new ChecksumService(new JobService());
        await Finish(service.Create(files, bench.Root, ChecksumAlgo.Sha256, false, output).Job);
        File.WriteAllText(files[0], "changed");
        File.Delete(files[1]);
        var verified = service.Verify([output]);
        var result = await Finish(verified.Job);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Empty(result.Failed);
        Assert.Equal(new ChecksumVerdict("a.txt", ChecksumVerdictKind.Mismatch), verified.Verdicts[0]);
        Assert.Equal(new ChecksumVerdict("sub/b.txt", ChecksumVerdictKind.Missing), verified.Verdicts[1]);
    }

    [Fact]
    public async Task OncePerFileUsesTheOutputNameRule()
    {
        using var bench = new Bench();
        var files = bench.Sample();
        var service = new ChecksumService(new JobService());
        var created = service.Create(files, bench.Root, ChecksumAlgo.Md5, true, "");
        var result = await Finish(created.Job);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Empty(result.Failed);
        Assert.Equal(2, created.LinesWritten);
        var first = File.ReadAllText(bench.PathOf("a.txt.md5"));
        var second = File.ReadAllText(bench.PathOf("sub", "b.txt.md5"));
        Assert.Contains("900150983cd24fb0d6963f7d28e17f72 *a.txt\n", first, StringComparison.Ordinal);
        Assert.Contains(" *b.txt\n", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateJobReportsAMissingFile()
    {
        using var bench = new Bench();
        var output = bench.PathOf("x.md5");
        var service = new ChecksumService(new JobService());
        var created = service.Create([bench.PathOf("nope.txt")], bench.Root, ChecksumAlgo.Md5, false, output);
        var result = await Finish(created.Job);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        var failed = Assert.Single(result.Failed);
        Assert.Equal(ErrorKind.NotFound, failed.Error.Kind);
        Assert.False(File.Exists(output));
    }

    private static string HashOf(string data, ChecksumAlgo algo)
    {
        using var bench = new Bench();
        var path = bench.PathOf("f");
        File.WriteAllText(path, data);
        return Checksum.HashFile(path, algo, Stop);
    }

    private static CancellationToken Stop => TestContext.Current.CancellationToken;

    private static async Task<JobResult> Finish(IJob job) =>
        await job.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);

    private sealed class Bench : IDisposable
    {
        public Bench()
        {
            Root = Path.Combine(Path.GetTempPath(), "d3sk-sum-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string PathOf(params string[] parts)
        {
            var path = Root;
            foreach (var part in parts)
            {
                path = Path.Combine(path, part);
            }

            return path;
        }

        public string[] Sample()
        {
            Directory.CreateDirectory(PathOf("sub"));
            var a = PathOf("a.txt");
            var b = PathOf("sub", "b.txt");
            File.WriteAllText(a, "abc");
            File.WriteAllText(b, "hello");
            return [a, b];
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
