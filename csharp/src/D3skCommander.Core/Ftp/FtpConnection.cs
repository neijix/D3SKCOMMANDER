using System.Net;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using FluentFTP;

namespace D3skCommander.Core.Ftp;

/// <summary>
/// A logged-in connection to one FTP or explicit-FTPS server. Failures come back as
/// <see cref="OperationError"/>, never as exceptions. One command runs at a time: a call waits
/// for the one before it, and a stream opened by <see cref="OpenReadAsync"/> or
/// <see cref="OpenWriteAsync"/> holds the connection until it is disposed.
/// </summary>
public sealed partial class FtpConnection : IAsyncDisposable
{
    private const int ConnectTimeoutMilliseconds = 15_000;
    private const int IoTimeoutMilliseconds = 60_000;
    private const int MaxDeleteDepth = 64;

    private readonly ListingClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _refusal;
    private bool _disposed;

    private FtpConnection(FtpSite site, ListingClient client)
    {
        Site = site;
        _client = client;
    }

    public FtpSite Site { get; }

    public bool IsConnected => !_disposed && _client.IsConnected;

    /// <summary>
    /// Connects and logs in. An empty host is refused without network. An empty user logs in
    /// anonymously (the password, when not empty, is then the e-mail address). With
    /// <see cref="FtpSite.Secure"/> the control and data connections are upgraded with
    /// <c>AUTH TLS</c> and a failing server certificate is refused with its reason.
    /// A wrong password is <see cref="ErrorKind.AccessDenied"/>.
    /// </summary>
    public static async Task<FileResult<FtpConnection>> ConnectAsync(
        FtpSite site,
        string password,
        FtpConnectOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(password);
        var host = site.Host.Trim();
        if (host.Length == 0)
        {
            return FileResult<FtpConnection>.Fail(OperationError.InvalidData("no host given"));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return FileResult<FtpConnection>.Fail(OperationError.Cancelled());
        }

        var connection = new FtpConnection(site, NewClient(site, host, password));
        connection.WatchCertificate(options?.TestTrustAnchors);
        var failure = await connection.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return FileResult<FtpConnection>.Fail(failure);
        }

        return FileResult<FtpConnection>.Ok(connection);
    }

    /// <summary>The current remote folder.</summary>
    public async Task<FileResult<string>> WorkingDirectoryAsync(CancellationToken cancellationToken)
    {
        using var turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
        if (turn.Error is not null)
        {
            return FileResult<string>.Fail(turn.Error);
        }

        try
        {
            return FileResult<string>.Ok(await _client.GetWorkingDirectory(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            return FileResult<string>.Fail(FtpErrors.Describe(ex, null));
        }
    }

    /// <summary>Says goodbye and closes. Errors are ignored: the connection is going away.</summary>
    public async Task DisconnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _client.Disconnect().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsFailure(ex))
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync().ConfigureAwait(false);
        _disposed = true;
        _client.Dispose();
        _gate.Dispose();
    }

    private static ListingClient NewClient(FtpSite site, string host, string password)
    {
        var anonymous = site.User.Length == 0;
        var user = anonymous ? "anonymous" : site.User;
        var secret = anonymous && password.Length == 0 ? "anonymous@" : password;
        var client = new ListingClient(host, new NetworkCredential(user, secret), site.EffectivePort);
        client.Config.ConnectTimeout = ConnectTimeoutMilliseconds;
        client.Config.ReadTimeout = IoTimeoutMilliseconds;
        client.Config.DataConnectionConnectTimeout = ConnectTimeoutMilliseconds;
        client.Config.DataConnectionReadTimeout = IoTimeoutMilliseconds;
        client.Config.RetryAttempts = 1;
        client.Config.DataConnectionType = site.Passive ? FtpDataConnectionType.AutoPassive : FtpDataConnectionType.AutoActive;
        client.Config.EncryptionMode = site.Secure ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;
        client.Config.DataConnectionEncryption = site.Secure;
        client.Config.ValidateAnyCertificate = false;
        return client;
    }

    private void WatchCertificate(System.Security.Cryptography.X509Certificates.X509Certificate2Collection? anchors)
    {
        _client.ValidateCertificate += (_, e) =>
        {
            var reason = FtpCertificatePolicy.Refusal(e.Certificate, e.Chain, e.PolicyErrors, anchors);
            e.Accept = reason is null;
            if (reason is not null)
            {
                _refusal ??= reason;
            }
        };
    }

    private async Task<OperationError?> OpenSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.Connect(cancellationToken).ConfigureAwait(false);
            if (Site.RemoteDir.Length > 0)
            {
                await _client.SetWorkingDirectory(Site.RemoteDir, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            if (_refusal is not null)
            {
                return FtpErrors.CertificateRefused(_refusal);
            }

            return FtpErrors.Describe(ex, null);
        }
    }

    private static bool IsFailure(Exception ex) => ex is not (OutOfMemoryException or StackOverflowException);
}
