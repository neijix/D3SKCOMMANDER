using System.Security.Cryptography.X509Certificates;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Ftp;
using Xunit;

namespace D3skCommander.Core.Tests.Ftp;

public class FtpsTests : IDisposable
{
    private readonly TestCertificates _certificates = new();

    public void Dispose() => _certificates.Dispose();

    [Fact]
    public async Task TrustedCertificateConnectsAndLists()
    {
        using var host = new FtpTestSite(_certificates.Server());
        File.WriteAllText(host.PathOf("secret.txt"), "x");
        var result = await Connect(host, _certificates.Anchors);
        Assert.True(result.IsSuccess, result.Error?.Message);
        await using var connection = result.Value;
        var listed = await connection.ListAsync("/", CancellationToken.None);
        Assert.True(listed.IsSuccess, listed.Error?.Message);
        Assert.Equal(["secret.txt"], listed.Value.Select(e => e.Name).ToArray());
    }

    [Fact]
    public async Task TrustedCertificateTransfersOverTheEncryptedDataConnection()
    {
        using var host = new FtpTestSite(_certificates.Server());
        var local = Path.GetTempFileName();
        File.WriteAllBytes(local, [1, 2, 3, 4]);
        try
        {
            var result = await Connect(host, _certificates.Anchors);
            await using var connection = result.Value;
            var up = await connection.UploadAsync(local, "/up.bin", null, CancellationToken.None);
            Assert.True(up.IsSuccess, up.Error?.Message);
            Assert.Equal([1, 2, 3, 4], host.ReadReceived("up.bin"));
        }
        finally
        {
            File.Delete(local);
        }
    }

    [Fact]
    public async Task InvalidCertificateIsRefusedWithItsReason()
    {
        using var host = new FtpTestSite(_certificates.SelfSigned());
        var result = await Connect(host, _certificates.Anchors);
        AssertRefused(result.Error, "not trusted");
    }

    [Fact]
    public async Task UnknownCertificateIsRefusedWithoutTheTestAnchors()
    {
        using var host = new FtpTestSite(_certificates.Server());
        var result = await Connect(host, null);
        AssertRefused(result.Error, "not trusted");
    }

    [Fact]
    public async Task CertificateForAnotherHostIsRefusedWithItsReason()
    {
        using var host = new FtpTestSite(_certificates.Server("other.example", ipAddress: false));
        var result = await Connect(host, _certificates.Anchors);
        AssertRefused(result.Error, "does not match the host name");
    }

    [Fact]
    public async Task ExpiredCertificateIsRefusedWithItsReason()
    {
        using var host = new FtpTestSite(_certificates.Server(expired: true));
        var result = await Connect(host, _certificates.Anchors);
        AssertRefused(result.Error, "NotTimeValid");
    }

    [Fact]
    public async Task RefusedCertificateNamesItsFingerprint()
    {
        using var certificate = _certificates.SelfSigned();
        using var host = new FtpTestSite(certificate);
        var result = await Connect(host, _certificates.Anchors);
        Assert.NotNull(result.Error);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.RawData));
        Assert.Contains(fingerprint, result.Error.Message, StringComparison.Ordinal);
    }

    private static void AssertRefused(OperationError? error, string reason)
    {
        Assert.NotNull(error);
        Assert.Equal(ErrorKind.Network, error.Kind);
        Assert.StartsWith("server certificate refused: ", error.Message, StringComparison.Ordinal);
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    private static Task<FileResult<FtpConnection>> Connect(FtpTestSite host, X509Certificate2Collection? anchors) =>
        FtpConnection.ConnectAsync(
            host.Site,
            FakeFtpServer.Password,
            new FtpConnectOptions { TestTrustAnchors = anchors },
            CancellationToken.None);
}
