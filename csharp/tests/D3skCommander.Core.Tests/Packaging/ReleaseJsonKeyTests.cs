using System.Security.Cryptography;
using System.Text.Json;
using D3skCommander.Core.Update;
using Xunit;

namespace D3skCommander.Core.Tests.Packaging;

public class ReleaseJsonKeyTests
{
    private static string RepoFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{name} not found above {AppContext.BaseDirectory}");
    }

    private static string Fingerprint(string pem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public void ReleaseJsonPublicKeyHasThePinnedFingerprint()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoFile("release.json")));
        var pem = doc.RootElement.GetProperty("signing").GetProperty("publicKeyPem").GetString()!;
        Assert.Equal(ReleaseKey.Fingerprint, Fingerprint(pem));
        Assert.Equal(ReleaseKey.Fingerprint, Fingerprint(ReleaseKey.PublicKeyPem));
    }
}
