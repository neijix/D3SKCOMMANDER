using D3sk.Update.SignIn;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Update;

namespace D3skCommander.App.Features.Update.SignIn;

/// <summary>
/// One person-started GitHub device sign-in. It asks <see cref="UpdateService"/> for the code, opens the device page only
/// when <see cref="SignInPolicy.MayOpen"/> allows it, and follows the sign-in as a job of the job service until it ends.
/// <see cref="Close"/> (the window closing) cancels the job and the sign-in: no request follows and nothing is stored.
/// </summary>
public sealed class SignInFlow : IDisposable
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(150);

    private readonly UpdateService _service;
    private readonly IJobService _jobs;
    private readonly IUrlOpener _opener;
    private IJob? _job;
    private volatile bool _closed;

    public SignInFlow(UpdateService service, IJobService jobs, IUrlOpener opener)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(opener);
        _service = service;
        _jobs = jobs;
        _opener = opener;
    }

    /// <summary>Raised on a worker thread for each state the person should see: waiting (with the code) and the end.</summary>
    public event Action<SignInState>? Changed;

    /// <summary>The job following the sign-in; null before <see cref="StartAsync"/> got a code.</summary>
    public IJob? Job => _job;

    public async Task StartAsync()
    {
        if (_closed)
        {
            return;
        }

        var state = await _service.StartSignInAsync(userInitiated: true).ConfigureAwait(true);
        if (_closed)
        {
            _service.CancelSignIn();
            return;
        }

        if (state.Phase != SignInPhase.Waiting)
        {
            Changed?.Invoke(state);
            return;
        }

        if (SignInPolicy.MayOpen(SignInPolicy.VerificationUrl))
        {
            _opener.Open(SignInPolicy.VerificationUrl);
        }

        Changed?.Invoke(state);
        _job = _jobs.Start("GitHub sign-in", (_, token) => WatchAsync(token));
    }

    /// <summary>Cancels the job and any waiting sign-in; later calls and events do nothing.</summary>
    public void Close()
    {
        _closed = true;
        _job?.Cancel();
        _service.CancelSignIn();
    }

    public void Dispose() => Close();

    private async Task WatchAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var state = _service.GetSignInState();
                if (state.Phase != SignInPhase.Waiting)
                {
                    if (!_closed)
                    {
                        Changed?.Invoke(state);
                    }

                    return;
                }

                await Task.Delay(WatchInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _service.CancelSignIn();
            throw;
        }
    }
}
