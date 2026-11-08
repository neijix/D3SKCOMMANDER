using System.Security.Cryptography;
using D3sk.Update.Keychain;
using D3sk.Update.SignIn;
using D3sk.Update.Token;
using D3sk.Update.UpdateClient;
using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using LibraryConfig = D3sk.Update.UpdateClient.UpdateConfig;

namespace D3skCommander.Core.Update;

/// <summary>What a check found; <see cref="KeychainReason"/> is the raw reason when the status is keychain unavailable.</summary>
public sealed record UpdateCheck(CheckStatus Status, string? Reason, UpdateOffer? Offer, string? KeychainReason);

/// <summary>
/// The app's update flow: <see cref="CheckAsync"/> and <see cref="Download(UpdateOffer, string, CancellationToken)"/> run
/// D3sk.Update's signed updater; the older <see cref="Download(ReleaseAsset, string, string?, CancellationToken)"/> stays
/// until the app switches. Downloads run on the job service.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly IJobService _jobs;
    private readonly IAssetSource? _source;
    private readonly IDisposable? _owned;
    private readonly ReleaseUpdater? _updater;
    private readonly DeviceSignIn? _signIn;

    public UpdateService(IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var http = new HttpAssetSource(UpdateMetadata.CurrentVersion);
        _jobs = jobs;
        _source = http;
        _owned = http;
    }

    public UpdateService(IJobService jobs, IAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(source);
        _jobs = jobs;
        _source = source;
    }

    /// <summary>The signed flow. <paramref name="http"/> and <paramref name="keychain"/> belong to the caller.</summary>
    /// <param name="jobs">Runs the downloads.</param>
    /// <param name="config">From <see cref="UpdateConfig.Create"/>.</param>
    /// <param name="http">The HTTP client for the API, github.com and the download.</param>
    /// <param name="keychain">Holds the token pair.</param>
    /// <param name="progress">One event per step (never a token); null for none.</param>
    /// <param name="setting">Reads the client-ID override variable; the process environment when null.</param>
    /// <param name="time">The sign-in's clock; the system one when null.</param>
    public UpdateService(
        IJobService jobs,
        LibraryConfig config,
        HttpClient http,
        IKeychain keychain,
        IProgress<UpdateEvent>? progress = null,
        Func<string, string?>? setting = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(keychain);
        _jobs = jobs;
        var updater = new ReleaseUpdater(config, http, keychain, progress, null, setting);
        _updater = updater;
        _signIn = new DeviceSignIn(new GitHubOAuth(http, config.UserAgent), updater.Tokens, () => updater.ClientId, time ?? TimeProvider.System);
    }

    /// <summary>
    /// Looks for a release newer than <paramref name="currentVersion"/> with this platform's package. Never throws for a
    /// network, sign-in or keychain problem and never starts a sign-in: the status says it.
    /// </summary>
    public async Task<UpdateCheck> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        var result = await Signed().CheckAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        string? keychain = null;
        if (result.Status == CheckStatus.KeychainUnavailable)
        {
            var prefix = ReleaseTokenStore.UnavailableText("");
            keychain = result.Reason is { } text && text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : result.Reason;
        }

        return new UpdateCheck(result.Status, result.Reason, result.Offer, keychain);
    }

    /// <summary>Where the sign-in stands (the waiting state carries the user code to show).</summary>
    public SignInState GetSignInState() => Sign().State();

    /// <summary>
    /// Starts the GitHub device flow when <see cref="SignInPolicy.MayStartDeviceFlow"/> allows it; a quiet check
    /// (<paramref name="userInitiated"/> false) only reads the state.
    /// </summary>
    public Task<SignInState> StartSignInAsync(bool userInitiated, CancellationToken cancellationToken = default) =>
        SignInPolicy.MayStartDeviceFlow(userInitiated) ? Sign().StartAsync(cancellationToken) : Task.FromResult(Sign().State());

    /// <summary>Stops a waiting sign-in (no request follows); stored tokens stay.</summary>
    public SignInState CancelSignIn() => Sign().Cancel();

    /// <summary>Cancels any sign-in and forgets the stored tokens.</summary>
    public SignInState SignOut() => Sign().SignOut();

    /// <summary>
    /// Queues the verified download of <paramref name="offer"/> into <paramref name="directory"/>: the signed sums and their
    /// signature first, then the package; nothing unverified is left. A refusal sets <see cref="UpdateDownload.Refusal"/>.
    /// </summary>
    public UpdateDownload Download(UpdateOffer offer, string directory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var updater = Signed();
        var session = new DownloadSession();
        var name = offer.Asset.Name;
        var job = StartJob(name, cancellationToken, async (progress, token) =>
        {
            Directory.CreateDirectory(directory);
            var bytes = new BytesProgress(done => progress.Report(new JobProgress(0, 1, done, 0, name)));
            var result = await updater.DownloadAsync(offer, directory, bytes, token).ConfigureAwait(false);
            switch (result.Status)
            {
                case DownloadStatus.Verified:
                    session.Path = result.Path;
                    return ItemResult.Success;
                case DownloadStatus.Refused:
                    session.Refusal = result.Refusal;
                    session.Reason = result.Reason;
                    return ItemResult.Failed(OperationError.InvalidData(result.Reason ?? name + ": refused", name));
                default:
                    session.Reason = result.Reason;
                    return ItemResult.Failed(OperationError.Network(result.Reason ?? name + ": download failed", name));
            }
        });
        return new UpdateDownload(job, session);
    }

    /// <summary>
    /// Queues the download of <paramref name="asset"/> into <paramref name="directory"/>.
    /// <paramref name="sums"/> is the <c>SHA256SUMS</c> text, used when the asset has no digest.
    /// A missing checksum or a mismatch removes nothing that was already installed; the partial file is removed.
    /// </summary>
    public UpdateDownload Download(
        ReleaseAsset asset,
        string directory,
        string? sums,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var session = new DownloadSession();
        var job = StartJob(asset.Name, cancellationToken, (progress, token) =>
            SaveAsync(asset, directory, sums, progress, token, session));
        return new UpdateDownload(job, session);
    }

    public void Dispose() => _owned?.Dispose();

    private ReleaseUpdater Signed() =>
        _updater ?? throw new InvalidOperationException("this UpdateService was built without a signed-update configuration");

    private DeviceSignIn Sign() =>
        _signIn ?? throw new InvalidOperationException("this UpdateService was built without a signed-update configuration");

    private IJob StartJob(string name, CancellationToken cancellationToken, Func<IJobProgress, CancellationToken, Task<ItemResult>> body)
    {
        var ready = new TaskCompletionSource<IJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = _jobs.Start("Download", [new JobItem(name)], async (_, progress, token) =>
        {
            var self = await ready.Task.WaitAsync(token).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            try
            {
                if (linked.IsCancellationRequested)
                {
                    self.Cancel();
                    throw new OperationCanceledException(linked.Token);
                }

                return await body(progress, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                self.Cancel();
                throw;
            }
        });
        ready.SetResult(job);
        return job;
    }

    private async Task<ItemResult> SaveAsync(
        ReleaseAsset asset,
        string directory,
        string? sums,
        IJobProgress progress,
        CancellationToken cancellationToken,
        DownloadSession session)
    {
        var expected = ReleaseChecksum.Expected(asset, sums);
        if (expected is null)
        {
            return ItemResult.Failed(OperationError.InvalidData(
                asset.Name + ": no checksum published, not installed",
                asset.Name));
        }

        Directory.CreateDirectory(directory);
        var dest = Path.Combine(directory, asset.Name);
        try
        {
            var got = await CopyAsync(asset, dest, progress, cancellationToken).ConfigureAwait(false);
            return Finish(asset, dest, expected, got, session);
        }
        catch (OperationCanceledException)
        {
            Delete(dest);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return ItemResult.Failed(OperationError.Network(ex.Message, asset.Name));
        }
    }

    private async Task<string> CopyAsync(
        ReleaseAsset asset,
        string dest,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var source = _source ?? throw new InvalidOperationException("this UpdateService has no asset source");
        await using var input = await source.OpenAsync(asset.Url, cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            dest,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long done = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            hasher.AppendData(buffer.AsSpan(0, count));
            done += count;
            progress.Report(new JobProgress(0, 1, done, asset.Size, asset.Name));
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return ReleaseChecksum.Format(hasher.GetHashAndReset());
    }

    private static ItemResult Finish(
        ReleaseAsset asset,
        string dest,
        string expected,
        string got,
        DownloadSession session)
    {
        if (!string.Equals(got, expected, StringComparison.Ordinal))
        {
            Delete(dest);
            return ItemResult.Failed(OperationError.InvalidData(
                asset.Name + ": checksum mismatch (expected " + expected + ", got " + got + ")",
                asset.Name));
        }

        session.Path = dest;
        return ItemResult.Success;
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked partial file is left for the caller to see.
        }
    }

    private sealed class BytesProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
