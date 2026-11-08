using System.Net;
using System.Security.Cryptography;
using System.Text;
using D3sk.Update.Keychain;
using D3sk.Update.Signature;
using D3sk.Update.UpdateClient;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Update;
using Xunit;
using AppConfig = D3skCommander.Core.Update.UpdateConfig;
using LibraryConfig = D3sk.Update.UpdateClient.UpdateConfig;

namespace D3skCommander.Core.Tests.Update;

/// <summary>
/// The download of a signed release runs as a job: progress, cancel and a hash mismatch leave no partial file.
/// A release listing with the package, its sums and the signature of a key that exists only in the test.
/// </summary>
public class UpdateDownloadTests
{
    private const string Api = "https://api.github.com/repos/neijix/D3SKCOMMANDER/releases";
    private const string Folder = "https://github.com/neijix/D3SKCOMMANDER/releases/download/v0.3.0/";
    private const string PackageName = "d3skcommander-0.3.0-linux-x86_64.tar.gz";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheVerifiedPackageIsSavedWithItsProgress()
    {
        var bytes = Encoding.ASCII.GetBytes(new string('p', 150_000));
        using var dir = new WorkDir();
        using var world = new World(bytes, _ => new ByteArrayContent(bytes));
        var offer = await world.OfferAsync();

        var download = world.Service.Download(offer, dir.Path, Ct);
        var result = await download.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(JobOutcome.Completed, result.Outcome);
        Assert.Empty(result.Failed);
        Assert.Equal(Path.Combine(dir.Path, PackageName), download.Path);
        Assert.Equal(bytes, File.ReadAllBytes(download.Path!));
        Assert.Equal(bytes.Length, download.Job.Progress.BytesDone);
    }

    [Fact]
    public async Task AHashMismatchRemovesTheFile()
    {
        var bytes = Encoding.ASCII.GetBytes("the signed package");
        using var dir = new WorkDir();
        using var world = new World(bytes, _ => new ByteArrayContent("something else"u8.ToArray()));
        var offer = await world.OfferAsync();

        var download = world.Service.Download(offer, dir.Path, Ct);
        var result = await download.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Single(result.Failed);
        Assert.Equal(SumsRefusal.HashMismatch, download.Refusal);
        Assert.Null(download.Path);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public async Task CancellingTheJobStopsTheDownloadAndLeavesNoPartialFile()
    {
        var bytes = Encoding.ASCII.GetBytes(new string('p', 150_000));
        using var dir = new WorkDir();
        var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var world = new World(bytes, _ => new StreamContent(new StallingStream(bytes.AsMemory(0, 70_000), stalled)));
        var offer = await world.OfferAsync();

        var download = world.Service.Download(offer, dir.Path, Ct);
        await stalled.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        download.Job.Cancel();
        var result = await download.Job.Completion.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(JobOutcome.Cancelled, result.Outcome);
        Assert.Null(download.Path);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    /// <summary>Gives <paramref name="head"/>, tells <paramref name="stalled"/>, then waits until the read is cancelled.</summary>
    private sealed class StallingStream(ReadOnlyMemory<byte> head, TaskCompletionSource stalled) : Stream
    {
        private int _given;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_given < head.Length)
            {
                var count = Math.Min(buffer.Length, head.Length - _given);
                head.Slice(_given, count).CopyTo(buffer);
                _given += count;
                return count;
            }

            stalled.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class World : IDisposable
    {
        private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public World(byte[] signedPackage, Func<HttpRequestMessage, HttpContent> package)
        {
            var sums = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(signedPackage)) + "  " + PackageName + "\n");
            var signature = ReleaseSignature.Sign(sums, _signer.ExportPkcs8PrivateKeyPem());
            var listing = "[{\"tag_name\":\"v0.3.0\",\"draft\":false,\"prerelease\":false,\"assets\":[" + string.Join(",", new[]
            {
                Asset(PackageName, 1), Asset("SHA256SUMS", 2), Asset("SHA256SUMS.sig", 3),
            }) + "]}]";
            var github = new FakeGitHub()
                .On("GET", Api, HttpStatusCode.OK, listing)
                .On("GET", Folder + PackageName, request => new HttpResponseMessage(HttpStatusCode.OK) { Content = package(request) })
                .On("GET", Folder + "SHA256SUMS", HttpStatusCode.OK, sums)
                .On("GET", Folder + "SHA256SUMS.sig", HttpStatusCode.OK, signature);
            var pem = _signer.ExportSubjectPublicKeyInfoPem();
            var config = new LibraryConfig(
                AppConfig.Repo, AppConfig.TagPrefix, "d3skcommander/0.2.1", pem, D3sk.Update.Signature.ReleaseKey.Fingerprint(pem)!,
                AppConfig.ClientIdEnvName, AppConfig.KeychainService, version => "d3skcommander-" + version + "-linux-x86_64.tar.gz");
            Service = new UpdateService(new JobService(), config, github.Client(), new MemoryKeychain(), setting: _ => null);
        }

        public UpdateService Service { get; }

        public async Task<UpdateOffer> OfferAsync()
        {
            var check = await Service.CheckAsync("0.2.1", Ct);
            Assert.Equal(CheckStatus.Newer, check.Status);
            return check.Offer!;
        }

        public void Dispose()
        {
            Service.Dispose();
            _signer.Dispose();
        }

        private static string Asset(string name, int id) =>
            "{\"name\":\"" + name + "\",\"url\":\"https://api.github.com/repos/neijix/D3SKCOMMANDER/releases/assets/" + id
            + "\",\"browser_download_url\":\"" + Folder + name + "\"}";
    }
}
