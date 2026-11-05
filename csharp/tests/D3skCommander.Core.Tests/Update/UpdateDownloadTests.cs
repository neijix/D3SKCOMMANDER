using System.Text;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Update;
using Xunit;

namespace D3skCommander.Core.Tests.Update;

/// <summary>Download runs as a job and checks SHA-256 from the asset digest or from <c>SHA256SUMS</c>.</summary>
public class UpdateDownloadTests
{
    [Fact]
    public async Task DownloadChecksTheAssetDigest()
    {
        var bytes = "payload"u8.ToArray();
        var hash = ReleaseChecksum.Of(bytes);
        var asset = new ReleaseAsset("app.bin", "https://example.invalid/app.bin", bytes.Length, hash);
        using var dir = new WorkDir();
        var service = new UpdateService(new JobService(), new BytesSource(bytes));
        var download = service.Download(asset, dir.Path, sums: null, TestContext.Current.CancellationToken);
        var result = await download.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Empty(result.Failed);
        var path = download.Path;
        Assert.NotNull(path);
        Assert.Equal(Path.Combine(dir.Path, "app.bin"), path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(bytes.Length, download.Job.Progress.BytesDone);
    }

    [Fact]
    public async Task DownloadChecksSha256SumsWhenTheAssetHasNoDigest()
    {
        var bytes = "from-sums"u8.ToArray();
        var hash = ReleaseChecksum.Of(bytes);
        var asset = new ReleaseAsset("app.bin", "https://example.invalid/app.bin", bytes.Length, null);
        var sums = hash + "  app.bin\n";
        using var dir = new WorkDir();
        var service = new UpdateService(new JobService(), new BytesSource(bytes));
        var download = service.Download(asset, dir.Path, sums, TestContext.Current.CancellationToken);
        var result = await download.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Empty(result.Failed);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(dir.Path, "app.bin")));
    }

    [Fact]
    public async Task AChecksumMismatchRemovesTheFile()
    {
        var bytes = "nope"u8.ToArray();
        var asset = new ReleaseAsset("app.bin", "https://example.invalid/app.bin", bytes.Length, new string('0', 64));
        using var dir = new WorkDir();
        var service = new UpdateService(new JobService(), new BytesSource(bytes));
        var download = service.Download(asset, dir.Path, sums: null, TestContext.Current.CancellationToken);
        var result = await download.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, result.Outcome);
        var failed = Assert.Single(result.Failed);
        Assert.Contains("checksum mismatch", failed.Error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(dir.Path, "app.bin")));
        Assert.Null(download.Path);
    }

    [Fact]
    public async Task AFileWithoutAChecksumIsRefused()
    {
        var asset = new ReleaseAsset("app.bin", "https://example.invalid/app.bin", 1, null);
        using var dir = new WorkDir();
        var service = new UpdateService(new JobService(), new BytesSource("x"u8.ToArray()));
        var download = service.Download(asset, dir.Path, sums: null, TestContext.Current.CancellationToken);
        var result = await download.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var failed = Assert.Single(result.Failed);
        Assert.Contains("no checksum published", failed.Error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(dir.Path, "app.bin")));
    }

    [Fact]
    public void Sha256SumsAcceptsTheBinaryMark()
    {
        var hash = new string('a', 64);
        var sums = new StringBuilder().Append(hash).Append(" *app.bin\n").ToString();
        Assert.Equal(hash, ReleaseChecksum.LineFor(sums, "app.bin"));
    }
}
