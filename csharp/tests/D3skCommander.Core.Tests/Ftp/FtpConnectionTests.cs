using D3skCommander.Core.Common;
using D3skCommander.Core.Ftp;
using Xunit;

namespace D3skCommander.Core.Tests.Ftp;

public class FtpConnectionTests
{
    [Fact]
    public async Task ListUsesMlsdAndPutsFoldersFirst()
    {
        using var host = new FtpTestSite();
        File.WriteAllText(host.PathOf("b.txt"), "bb");
        File.WriteAllText(host.PathOf("A.txt"), "a");
        Directory.CreateDirectory(host.PathOf("zdir"));
        await using var connection = await Connect(host);
        var listed = await connection.ListAsync("/", CancellationToken.None);
        Assert.True(listed.IsSuccess, listed.Error?.Message);
        Assert.Equal(["zdir", "A.txt", "b.txt"], listed.Value.Select(e => e.Name).ToArray());
        Assert.True(listed.Value[0].IsDirectory);
        Assert.Equal(2UL, listed.Value[2].Size);
        Assert.NotNull(listed.Value[2].Modified);
    }

    [Fact]
    public async Task ListFallsBackToListWhenTheServerHasNoMlsd()
    {
        using var host = new FtpTestSite(withMlsd: false);
        File.WriteAllText(host.PathOf("b.txt"), "bb");
        Directory.CreateDirectory(host.PathOf("zdir"));
        await using var connection = await Connect(host);
        var listed = await connection.ListAsync("/", CancellationToken.None);
        Assert.True(listed.IsSuccess, listed.Error?.Message);
        Assert.Equal(["zdir", "b.txt"], listed.Value.Select(e => e.Name).ToArray());
        Assert.True(listed.Value[0].IsDirectory);
        Assert.Equal(2UL, listed.Value[1].Size);
    }

    [Fact]
    public async Task ListOfAMissingFolderIsNotFound()
    {
        using var host = new FtpTestSite();
        await using var connection = await Connect(host);
        var listed = await connection.ListAsync("/nowhere", CancellationToken.None);
        Assert.Equal(ErrorKind.NotFound, listed.Error?.Kind);
        Assert.Equal("/nowhere", listed.Error?.Path);
    }

    [Fact]
    public async Task RemoteDirOfTheSiteIsEnteredAfterLogin()
    {
        using var host = new FtpTestSite();
        Directory.CreateDirectory(host.PathOf("inner"));
        host.Site.RemoteDir = "/inner";
        await using var connection = await Connect(host);
        var directory = await connection.WorkingDirectoryAsync(CancellationToken.None);
        Assert.True(directory.IsSuccess, directory.Error?.Message);
        Assert.Equal("/inner", directory.Value);
    }

    [Fact]
    public async Task WrongPasswordIsAnAccessDeniedError()
    {
        using var host = new FtpTestSite();
        var result = await FtpConnection.ConnectAsync(host.Site, "wrong", null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.AccessDenied, result.Error?.Kind);
    }

    [Fact]
    public async Task EmptyHostIsRefusedWithoutNetwork()
    {
        var site = new FtpSite { Host = "   " };
        var result = await FtpConnection.ConnectAsync(site, "", null, CancellationToken.None);
        Assert.Equal(ErrorKind.InvalidData, result.Error?.Kind);
        Assert.Contains("host", result.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreachableServerIsANetworkError()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var site = new FtpSite { Host = "127.0.0.1", Port = port };
        var result = await FtpConnection.ConnectAsync(site, "", null, CancellationToken.None);
        Assert.Equal(ErrorKind.Network, result.Error?.Kind);
    }

    [Fact]
    public async Task DownloadWritesTheFileAndReportsProgress()
    {
        using var host = new FtpTestSite();
        var payload = Payload(300_000);
        File.WriteAllBytes(host.PathOf("big.bin"), payload);
        using var local = new LocalFolder();
        await using var connection = await Connect(host);
        var progress = new Recorder();
        var target = local.PathOf("big.bin");
        var result = await connection.DownloadAsync("/big.bin", target, progress, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(payload.Length, result.Value.Bytes);
        Assert.Equal(payload, File.ReadAllBytes(target));
        Assert.Equal(payload.Length, progress.Last);
        Assert.Equal(["big.bin"], local.Names());
    }

    [Fact]
    public async Task DownloadOfAMissingFileIsNotFoundAndLeavesNothing()
    {
        using var host = new FtpTestSite();
        using var local = new LocalFolder();
        await using var connection = await Connect(host);
        var result = await connection.DownloadAsync("/missing.bin", local.PathOf("missing.bin"), null, CancellationToken.None);
        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
        Assert.Empty(local.Names());
    }

    [Fact]
    public async Task UploadSendsTheFileAndReportsProgress()
    {
        using var host = new FtpTestSite();
        using var local = new LocalFolder();
        var payload = Payload(300_000);
        File.WriteAllBytes(local.PathOf("up.bin"), payload);
        await using var connection = await Connect(host);
        var progress = new Recorder();
        var result = await connection.UploadAsync(local.PathOf("up.bin"), "/up.bin", progress, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(payload.Length, result.Value.Bytes);
        Assert.Equal(payload, host.ReadReceived("up.bin"));
        Assert.Equal(payload.Length, progress.Last);
    }

    [Fact]
    public async Task CancelledDownloadLeavesNoPartialFileAndKeepsTheConnection()
    {
        using var host = new FtpTestSite();
        File.WriteAllBytes(host.PathOf("big.bin"), Payload(20_000_000));
        using var local = new LocalFolder();
        await using var connection = await Connect(host);
        using var cts = new CancellationTokenSource();
        var progress = new Recorder(cts);
        var result = await connection.DownloadAsync("/big.bin", local.PathOf("big.bin"), progress, cts.Token);
        Assert.Equal(ErrorKind.Cancelled, result.Error?.Kind);
        Assert.Empty(local.Names());
        Assert.True(progress.Last < 20_000_000);
    }

    [Fact]
    public async Task CancelledUploadLeavesNoPartialRemoteFile()
    {
        using var host = new FtpTestSite();
        using var local = new LocalFolder();
        File.WriteAllBytes(local.PathOf("big.bin"), Payload(20_000_000));
        await using var connection = await Connect(host);
        using var cts = new CancellationTokenSource();
        var result = await connection.UploadAsync(local.PathOf("big.bin"), "/big.bin", new Recorder(cts), cts.Token);
        Assert.Equal(ErrorKind.Cancelled, result.Error?.Kind);
        Assert.False(File.Exists(host.PathOf("big.bin")));
    }

    [Fact]
    public async Task ConnectionStaysUsableAfterACancelledTransfer()
    {
        using var host = new FtpTestSite();
        File.WriteAllBytes(host.PathOf("big.bin"), Payload(20_000_000));
        using var local = new LocalFolder();
        await using var connection = await Connect(host);
        using var cts = new CancellationTokenSource();
        await connection.DownloadAsync("/big.bin", local.PathOf("big.bin"), new Recorder(cts), cts.Token);
        var listed = await connection.ListAsync("/", CancellationToken.None);
        Assert.True(listed.IsSuccess, listed.Error?.Message);
        Assert.Equal(["big.bin"], listed.Value.Select(e => e.Name).ToArray());
    }

    [Fact]
    public async Task CreateFolderRenameAndDeleteWork()
    {
        using var host = new FtpTestSite();
        File.WriteAllText(host.PathOf("a.txt"), "a");
        await using var connection = await Connect(host);
        var created = await connection.CreateFolderAsync("/made", CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.True(Directory.Exists(host.PathOf("made")));
        var again = await connection.CreateFolderAsync("/made", CancellationToken.None);
        Assert.Equal(ErrorKind.AlreadyExists, again.Error?.Kind);

        var renamed = await connection.RenameAsync("/a.txt", "b.txt", CancellationToken.None);
        Assert.True(renamed.IsSuccess, renamed.Error?.Message);
        Assert.Equal("/b.txt", renamed.Value);
        Assert.True(File.Exists(host.PathOf("b.txt")));
        Assert.False(File.Exists(host.PathOf("a.txt")));

        var deleted = await connection.DeleteAsync("/b.txt", CancellationToken.None);
        Assert.True(deleted.IsSuccess, deleted.Error?.Message);
        Assert.False(File.Exists(host.PathOf("b.txt")));
    }

    [Fact]
    public async Task DeleteRemovesAFolderWithItsContent()
    {
        using var host = new FtpTestSite();
        Directory.CreateDirectory(host.PathOf("tree/sub"));
        File.WriteAllText(host.PathOf("tree/one.txt"), "1");
        File.WriteAllText(host.PathOf("tree/sub/two.txt"), "2");
        await using var connection = await Connect(host);
        var deleted = await connection.DeleteAsync("/tree", CancellationToken.None);
        Assert.True(deleted.IsSuccess, deleted.Error?.Message);
        Assert.False(Directory.Exists(host.PathOf("tree")));
    }

    [Fact]
    public async Task RenameRefusesAnUnsafeNameWithoutTouchingTheServer()
    {
        using var host = new FtpTestSite();
        File.WriteAllText(host.PathOf("a.txt"), "a");
        await using var connection = await Connect(host);
        foreach (var name in new[] { "..", "x/y", "a\\b", "" })
        {
            var renamed = await connection.RenameAsync("/a.txt", name, CancellationToken.None);
            Assert.Equal(ErrorKind.InvalidData, renamed.Error?.Kind);
        }

        Assert.True(File.Exists(host.PathOf("a.txt")));
    }

    internal static async Task<FtpConnection> Connect(FtpTestSite host)
    {
        var result = await FtpConnection.ConnectAsync(host.Site, FakeFtpServer.Password, null, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private static byte[] Payload(int length)
    {
        var bytes = new byte[length];
        new Random(7).NextBytes(bytes);
        return bytes;
    }

    /// <summary>Records progress; given a source, cancels it at the first report.</summary>
    private sealed class Recorder : IProgress<long>
    {
        private readonly CancellationTokenSource? _cancel;

        public Recorder(CancellationTokenSource? cancel = null) => _cancel = cancel;

        public long Last { get; private set; }

        public void Report(long value)
        {
            Last = value;
            _cancel?.Cancel();
        }
    }

    private sealed class LocalFolder : IDisposable
    {
        private readonly string _path = Directory.CreateTempSubdirectory("d3sk-ftp-local-").FullName;

        public string PathOf(string name) => Path.Combine(_path, name);

        public string[] Names() =>
            Directory.GetFileSystemEntries(_path).Select(Path.GetFileName).OfType<string>().Order().ToArray();

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
