using D3skCommander.Core.Ftp;

namespace D3skCommander.Core.Tests.Ftp;

/// <summary>A temp folder served by a <see cref="FakeFtpServer"/>, and the site that points at it.</summary>
public sealed class FtpTestSite : IDisposable
{
    private readonly FakeFtpServer _server;

    public FtpTestSite(System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null, bool withMlsd = true)
    {
        Root = Directory.CreateTempSubdirectory("d3sk-ftp-").FullName;
        _server = FakeFtpServer.Start(Root, certificate, withMlsd);
        Site = new FtpSite
        {
            Name = "test",
            Host = "127.0.0.1",
            Port = _server.Port,
            User = FakeFtpServer.User,
            Secure = certificate is not null,
        };
    }

    public string Root { get; }

    public FtpSite Site { get; }

    public string PathOf(string remote) => Path.Combine(Root, remote.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// The bytes of a file the server received. The server may still hold the file for a moment
    /// after it has answered the upload, so a locked file is retried for up to two seconds.
    /// </summary>
    public byte[] ReadReceived(string remote)
    {
        var path = PathOf(remote);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException) when (attempt < 40)
            {
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        _server.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
