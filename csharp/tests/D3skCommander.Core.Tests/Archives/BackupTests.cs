using D3skCommander.Core.Archives;
using D3skCommander.Core.Jobs;
using Xunit;

namespace D3skCommander.Core.Tests.Archives;

/// <summary>P15 core: the selection is backed up to a dated ZIP in the other folder.</summary>
public class BackupTests
{
    private static readonly DateTimeOffset Stamp = new(2020, 11, 4, 19, 5, 6, TimeSpan.Zero);

    [Fact]
    public void DatedNameUsesTheLocalStamp()
    {
        Assert.Equal("notes.txt-backup-20201104-190506.zip", BackupZip.FileName("notes.txt", Stamp));
    }

    [Fact]
    public void OneSourceUsesItsFileName()
    {
        Assert.Equal("notes.txt", BackupZip.BaseName(new[] { "/pane/notes.txt" }, "/pane"));
    }

    [Fact]
    public void SeveralSourcesUseTheActiveFolderName()
    {
        Assert.Equal("docs", BackupZip.BaseName(new[] { "/pane/a.txt", "/pane/b.txt" }, "/work/docs"));
    }

    [Fact]
    public void MissingNameFallsBackToBackup()
    {
        Assert.Equal("backup", BackupZip.BaseName(new[] { "a.txt", "b.txt" }, null));
        Assert.Equal("backup", BackupZip.BaseName(Array.Empty<string>(), ""));
    }

    [Fact]
    public async Task BackupPacksTheSelectionIntoThatZip()
    {
        using var bench = new Bench();
        var source = bench.PathOf("notes.txt");
        File.WriteAllText(source, "keep");
        var dest = bench.PathOf("other");
        Directory.CreateDirectory(dest);
        var service = new ArchiveService(new JobService());
        var job = service.Backup(dest, new[] { source }, null, Stamp, TestContext.Current.CancellationToken);
        var result = await job.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Empty(result.Failed);
        var archive = Path.Combine(dest, "notes.txt-backup-20201104-190506.zip");
        Assert.True(File.Exists(archive));
        var entries = ArchiveIo.List(archive).Value;
        Assert.Contains(entries, entry => entry.Path == "notes.txt" && !entry.IsDirectory);
        var extracted = bench.PathOf("out");
        Assert.Equal(1, Arc.Extract(archive, null, extracted).Value);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(extracted, "notes.txt")));
    }
}
