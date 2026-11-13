using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;

namespace D3skCommander.App.Features.NetDrives;

/// <summary>Connect and disconnect network drives. The OS calls run as jobs; the app never keeps the password (the OS
/// credential store may, when the user ticks Remember).</summary>
public sealed class NetDrivesCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly AppSettings _settings;
    private readonly INetDriveRunner _runner;
    private int _busy;

    public NetDrivesCommands(
        IPanelWorkspace panels,
        IDialogService dialogs,
        ILocalizer text,
        IJobService jobs,
        AppSettings settings,
        INetDriveRunner runner)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runner);
        _panels = panels;
        _dialogs = dialogs;
        _text = text;
        _jobs = jobs;
        _settings = settings;
        _runner = runner;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.NetConnect, () => _ = ConnectAsync());
        shell.Handle(CommandId.NetDisconnect, () => _ = DisconnectAsync());
    }

    public async Task ConnectAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await ConnectCore().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public async Task DisconnectAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await DisconnectCore().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task ConnectCore()
    {
        var form = new NetConnectForm(_text, _settings.Session.Current.NetShares, _runner.SupportsDriveLetter, _runner.SupportsServerLogin);
        while (await _dialogs.ShowFormAsync(form.Build()).ConfigureAwait(false))
        {
            var request = form.Read();
            if (request.Problem is not null)
            {
                await Fail(request.Problem).ConfigureAwait(false);
                continue;
            }

            await Mount(request).ConfigureAwait(false);
            return;
        }
    }

    private async Task Mount(NetConnectRequest request)
    {
        FileResult<string>? outcome = null;
        FileResult<string>? saved = null;
        var job = _jobs.Start(
            _text.Get("net.job_connect") + " " + request.Address,
            (_, _) =>
            {
                outcome = _runner.Connect(request.Share!, request.Credentials!, request.Letter);
                if (outcome.Value.IsSuccess && request.Remember)
                {
                    saved = _runner.Remember(request.Share!.Server, request.Credentials!);
                }

                return Task.CompletedTask;
            });
        await job.Completion.ConfigureAwait(false);
        if (outcome is null || !outcome.Value.IsSuccess)
        {
            await FailWith(outcome?.Error).ConfigureAwait(false);
            return;
        }

        var session = _settings.Session.Current;
        session.NetShares = NetDrive.Remember(session.NetShares, request.Address).ToList();
        _settings.Session.Save(session);
        await _panels.GoAsync(_panels.ActiveSide, outcome.Value.Value).ConfigureAwait(false);
        if (saved is { IsSuccess: false } failed)
        {
            await Fail(_text.Get("net.remember_failed") + "\n" + ErrorLines.Describe(_text, failed.Error!)).ConfigureAwait(false);
        }
    }

    private async Task DisconnectCore()
    {
        var title = _text.Get("cmd.net_disconnect");
        IReadOnlyList<ConnectedShare> shares = [];
        var listing = _jobs.Start(title, (_, _) =>
        {
            shares = _runner.ListConnected();
            return Task.CompletedTask;
        });
        await listing.Completion.ConfigureAwait(false);
        if (shares.Count == 0)
        {
            await _dialogs.ShowMessageAsync(title, _text.Get("net.none_connected")).ConfigureAwait(false);
            return;
        }

        var labels = shares.Select(share => share.Label).ToList();
        var picked = await _dialogs.ChooseAsync(title, _text.Get("net.choose_disconnect"), labels).ConfigureAwait(false);
        if (picked is null || picked.Value < 0 || picked.Value >= shares.Count)
        {
            return;
        }

        var target = shares[picked.Value];
        FileResult<string>? outcome = null;
        var job = _jobs.Start(title + " " + target.Label, (_, _) =>
        {
            outcome = _runner.Disconnect(target.Path);
            return Task.CompletedTask;
        });
        await job.Completion.ConfigureAwait(false);
        if (outcome is null || !outcome.Value.IsSuccess)
        {
            await FailWith(outcome?.Error).ConfigureAwait(false);
            return;
        }

        await _panels.RefreshAsync().ConfigureAwait(false);
    }

    private Task FailWith(OperationError? error)
    {
        var line = error is null ? _text.Get("error") : ErrorLines.Describe(_text, error);
        return Fail(line);
    }

    private Task Fail(string message) => _dialogs.ShowMessageAsync(_text.Get("error"), message);
}
