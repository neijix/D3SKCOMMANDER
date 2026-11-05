using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace D3skCommander.Core.Ftp;

/// <summary>
/// The FTPS trust rule of the port: a server certificate that does not validate is refused
/// with its reason and no prompt. The refusal is text the error shows.
/// </summary>
internal static class FtpCertificatePolicy
{
    /// <summary>Null when the certificate is acceptable, else the reason it is refused.</summary>
    public static string? Refusal(
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors,
        X509Certificate2Collection? anchors)
    {
        if (certificate is null || (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
        {
            return "the server sent no certificate";
        }

        var reasons = new List<string>();
        if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
        {
            reasons.Add("the certificate does not match the host name");
        }

        var chainProblem = anchors is null ? ChainProblem(chain, errors) : ChainProblemWith(certificate, anchors);
        if (chainProblem is not null)
        {
            reasons.Add(chainProblem);
        }

        if (reasons.Count == 0)
        {
            return null;
        }

        return string.Join("; ", reasons) + " (SHA-256 " + Fingerprint(certificate) + ")";
    }

    public static string Fingerprint(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

    private static string? ChainProblem(X509Chain? chain, SslPolicyErrors errors)
    {
        if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) == 0)
        {
            return null;
        }

        return Describe(chain?.ChainStatus ?? []);
    }

    private static string? ChainProblemWith(X509Certificate certificate, X509Certificate2Collection anchors)
    {
        using var copy = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(anchors);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        return chain.Build(copy) ? null : Describe(chain.ChainStatus);
    }

    private static string Describe(X509ChainStatus[] statuses)
    {
        if (statuses.Length == 0)
        {
            return "the certificate chain is not trusted";
        }

        var text = new StringBuilder("the certificate is not trusted (");
        for (var i = 0; i < statuses.Length; i++)
        {
            text.Append(i == 0 ? "" : ", ").Append(statuses[i].Status);
        }

        return text.Append(')').ToString();
    }
}
