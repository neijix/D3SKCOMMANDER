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
/// The app's update flow: <see cref="CheckAsync"/> and <see cref="Download"/> run D3sk.Update's signed updater;
/// downloads run on the job service.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly IJobService _jobs;
    private readonly ReleaseUpdater _updater;
    private readonly DeviceSignIn _signIn;

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
        var result = await _updater.CheckAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        string? keychain = null;
        if (result.Status == CheckStatus.KeychainUnavailable)
        {
            var prefix = ReleaseTokenStore.UnavailableText("");
            keychain = result.Reason is { } text && text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : result.Reason;
        }

        return new UpdateCheck(result.Status, result.Reason, result.Offer, keychain);
    }

    /// <summary>Where the sign-in stands (the waiting state carries the user code to show).</summary>
    public SignInState GetSignInState() => _signIn.State();

    /// <summary>
    /// Starts the GitHub device flow when <see cref="SignInPolicy.MayStartDeviceFlow"/> allows it; a quiet check
    /// (<paramref name="userInitiated"/> false) only reads the state.
    /// </summary>
    public Task<SignInState> StartSignInAsync(bool userInitiated, CancellationToken cancellationToken = default) =>
        SignInPolicy.MayStartDeviceFlow(userInitiated) ? _signIn.StartAsync(cancellationToken) : Task.FromResult(_signIn.State());

    /// <summary>Stops a waiting sign-in (no request follows); stored tokens stay.</summary>
    public SignInState CancelSignIn() => _signIn.Cancel();

    /// <summary>Cancels any sign-in and forgets the stored tokens.</summary>
    public SignInState SignOut() => _signIn.SignOut();

    /// <summary>
    /// Queues the verified download of <paramref name="offer"/> into <paramref name="directory"/>: the signed sums and their
    /// signature first, then the package; nothing unverified is left. A refusal sets <see cref="UpdateDownload.Refusal"/>.
    /// </summary>
    public UpdateDownload Download(UpdateOffer offer, string directory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var updater = _updater;
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

    /// <summary>Nothing is owned: the HTTP client and the keychain belong to the caller.</summary>
    public void Dispose()
    {
    }

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

    private sealed class BytesProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
