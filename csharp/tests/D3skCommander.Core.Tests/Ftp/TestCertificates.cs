using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace D3skCommander.Core.Tests.Ftp;

/// <summary>A throw-away CA and server certificates for the FTPS tests. Nothing touches the OS trust store.</summary>
public sealed class TestCertificates : IDisposable
{
    private readonly X509Certificate2 _ca;
    private readonly RSA _caKey;

    public TestCertificates()
    {
        _caKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=d3sk test CA", _caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        _ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-40), DateTimeOffset.UtcNow.AddDays(60));
        Anchors = [X509CertificateLoader.LoadCertificate(_ca.RawData)];
    }

    /// <summary>The CA as a public certificate, to pass as the test trust anchor.</summary>
    public X509Certificate2Collection Anchors { get; }

    /// <summary>A server certificate for 127.0.0.1 and localhost, signed by the CA.</summary>
    public X509Certificate2 Server(string dnsName = "localhost", bool ipAddress = true, bool expired = false) =>
        Issue(dnsName, ipAddress, expired, signer: true);

    /// <summary>A certificate for 127.0.0.1 signed by nobody the client knows.</summary>
    public X509Certificate2 SelfSigned() => Issue("localhost", true, false, signer: false);

    public void Dispose()
    {
        _ca.Dispose();
        _caKey.Dispose();
        foreach (var anchor in Anchors)
        {
            anchor.Dispose();
        }
    }

    private X509Certificate2 Issue(string dnsName, bool ipAddress, bool expired, bool signer)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + dnsName, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        if (ipAddress)
        {
            names.AddIpAddress(IPAddress.Loopback);
        }

        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var from = DateTimeOffset.UtcNow.AddDays(expired ? -20 : -1);
        var to = DateTimeOffset.UtcNow.AddDays(expired ? -10 : 10);
        X509Certificate2 certificate;
        if (signer)
        {
            var serial = RandomNumberGenerator.GetBytes(8);
            using var signed = request.Create(_ca, from, to, serial);
            certificate = signed.CopyWithPrivateKey(key);
        }
        else
        {
            certificate = request.CreateSelfSigned(from, to);
        }

        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}
