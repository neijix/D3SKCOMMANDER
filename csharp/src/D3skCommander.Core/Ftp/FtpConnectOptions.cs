using System.Security.Cryptography.X509Certificates;

namespace D3skCommander.Core.Ftp;

/// <summary>
/// How a connection is made, beyond the site. Never exposed in settings or the UI.
/// </summary>
public sealed class FtpConnectOptions
{
    /// <summary>
    /// Test-only: certificates trusted as roots in place of the operating system's store, so a
    /// test can trust its own CA without touching the machine. Null (the default) uses the system store.
    /// </summary>
    public X509Certificate2Collection? TestTrustAnchors { get; init; }
}
