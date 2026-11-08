namespace D3skCommander.Core.Update;

/// <summary>
/// The release signing public key this app pins (public material only; the private key never enters the repo). A fork
/// replaces <see cref="PublicKeyPem"/>, <see cref="Fingerprint"/>, the repository slug in <see cref="UpdateConfig"/>.
/// </summary>
public static class ReleaseKey
{
    /// <summary>The public key, PEM (ECDSA P-256).</summary>
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEpFkqu2s/vvxjjf5F31T7IXV1PZ3B
        E1NedRteqMAgfy5GYhijY6XM2+u1vHemElKTeAXZrAZ8N1a8l51PG7DJSQ==
        -----END PUBLIC KEY-----
        """;

    /// <summary>
    /// D3sk.Update's fingerprint of <see cref="PublicKeyPem"/>: SHA-256 of the SubjectPublicKeyInfo DER, lowercase hex.
    /// </summary>
    public const string Fingerprint = "97b20534abf9a0f42edf01b71a7c54a9bd69964f39dde9317a19b806a95bad37";
}
