using D3skCommander.Core.Archives;
using D3skCommander.Core.Jobs;
using Xunit;

namespace D3skCommander.Core.Tests.Archives;

/// <summary>Pack and extract report progress and cancel through the job service.</summary>
public class ArchiveJobTests
{
    [Fact]
    public async Task PackReportsTheCurrentName()
    {
        using var bench = new Bench();
        bench.MakeTree();
        var archive = bench.ArchivePath(ArchiveKind.Zip);
        var service = new ArchiveService(new JobService());
        var job = service.Pack(
            archive,
            ArchiveKind.Zip,
            new[] { bench.PathOf("proj") },
            6,
            TestContext.Current.CancellationToken);
        var result = await job.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Equal(3, job.FileCount);
        Assert.Contains(job.Names, name => name.Contains("top.txt", StringComparison.Ordinal));
        Assert.True(File.Exists(archive));
        Assert.False(File.Exists(archive + ".part"));
    }

    [Fact]
    public async Task CancelRemovesThePartialArchive()
    {
        using var bench = new Bench();
        bench.MakeTree();
        var archive = bench.ArchivePath(ArchiveKind.SevenZ);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var service = new ArchiveService(new JobService());
        var job = service.Pack(archive, ArchiveKind.SevenZ, new[] { bench.PathOf("proj") }, 6, cancel.Token);
        var result = await job.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Cancelled, result.Outcome);
        Assert.False(File.Exists(archive));
        Assert.False(File.Exists(archive + ".part"));
    }
}
