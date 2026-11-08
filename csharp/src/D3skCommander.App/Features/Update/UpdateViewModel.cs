using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3sk.Update.SignIn;
using D3sk.Update.Token;
using D3sk.Update.UpdateClient;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.Install;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Update;

namespace D3skCommander.App.Features.Update;

/// <summary>
/// The Update window: current and latest version, then download (a job with progress), check,
/// install per install kind and restart. A failed step shows a localized line, never the raw text.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject, IShellWidget
{
    /// <summary>How Core's not-writable error starts (<c>LinuxScriptUpdate.NotWritable</c>); the UI shows <c>update.not_writable</c> instead.</summary>
    private const string NotWritablePrefix = "Cannot update: ";

    private const string NotWritableSuffix = " is not writable.";

    private readonly UpdateChecker _checker;
    private readonly UpdateService _service;
    private readonly IUpdateRestarter _restarter;
    private readonly ILocalizer _text;
    private readonly string _folder;
    private readonly Action<UpdateCheck> _found;
    private readonly Func<InstallKind, bool> _needsAdministrator;
    private CancellationTokenSource? _download;
    private UpdateOffer? _offer;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _latest = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canInstall;

    [ObservableProperty]
    private bool _isDownloading;

    /// <param name="needsAdministrator">Core's decision for the install kind; <see cref="InstallElevation.NeedsAdministrator"/> when null.</param>
    public UpdateViewModel(
        UpdateChecker checker,
        UpdateService service,
        IUpdateRestarter restarter,
        ILocalizer text,
        string folder,
        Action<UpdateCheck> found,
        Func<InstallKind, bool>? needsAdministrator = null)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(restarter);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(found);
        _checker = checker;
        _service = service;
        _restarter = restarter;
        _text = text;
        _folder = folder;
        _found = found;
        _needsAdministrator = needsAdministrator ?? InstallElevation.NeedsAdministrator;
        CurrentText = text.Get("update.current") + ": " + checker.Environment.Version;
    }

    public string? Name => "update";

    public string CurrentText { get; }

    public string NotesTitle => _text.Get("update.notes");

    public string CheckAgainText => _text.Get("update.check_again");

    public string InstallText => _text.Get("update.install");

    public string CancelText => _text.Get("update.cancel");

    public Control CreateContent() => new UpdateView(this);

    /// <summary>The localized line for a sign-in that ended in <see cref="SignInPhase.Failed"/>: the keychain text when the keychain was the cause.</summary>
    public string SignInLine(SignInState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var reason = state.Reason ?? "";
        var prefix = ReleaseTokenStore.UnavailableText("");
        return reason.StartsWith(prefix, StringComparison.Ordinal)
            ? KeychainLine(reason[prefix.Length..])
            : _text.Get("update.failed") + " " + reason;
    }

    /// <summary>Reads the latest signed release and shows current against latest.</summary>
    [RelayCommand]
    public async Task CheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        CanInstall = false;
        Status = _text.Get("update.checking");
        try
        {
            var check = await _checker.CheckAsync().ConfigureAwait(true);
            Show(check);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Downloads the verified release file, installs it for this install kind and restarts.</summary>
    [RelayCommand]
    public async Task InstallAsync()
    {
        var offer = _offer;
        if (IsBusy || offer is null)
        {
            return;
        }

        IsBusy = true;
        CanInstall = false;
        try
        {
            var path = await DownloadAsync(offer).ConfigureAwait(true);
            if (path is not null)
            {
                await InstallFileAsync(path, offer.Version).ConfigureAwait(true);
            }
        }
        finally
        {
            IsDownloading = false;
            IsBusy = false;
            CanInstall = _offer is not null;
        }
    }

    [RelayCommand]
    public void CancelDownload() => _download?.Cancel();

    private void Show(UpdateCheck check)
    {
        _offer = check.Offer;
        _found(check);
        Latest = "";
        Notes = "";
        switch (check.Status)
        {
            case CheckStatus.Newer when check.Offer is { } offer:
                ShowOffer(offer);
                break;
            case CheckStatus.UpToDate:
                Status = _text.Get("update.up_to_date");
                break;
            case CheckStatus.SignInNeeded:
                Status = _text.Get("update.sign_in_needed");
                break;
            case CheckStatus.NoNetwork:
                Status = _text.Get("update.no_network");
                break;
            case CheckStatus.KeychainUnavailable:
                Status = KeychainLine(check.KeychainReason ?? "");
                break;
            default:
                Status = _text.Get("update.check_failed");
                break;
        }
    }

    private void ShowOffer(UpdateOffer offer)
    {
        Latest = _text.Get("update.latest") + ": " + offer.Version;
        Status = _text.Get("update.available").Replace("{version}", offer.Version, StringComparison.Ordinal);
        var kind = _checker.Environment.Kind;
        if (kind is InstallKind.Source)
        {
            Status += " " + _text.Get("update.source_build");
            return;
        }

        if (_needsAdministrator(kind))
        {
            Status += " " + _text.Get("update.needs_admin");
        }

        CanInstall = true;
    }

    private string KeychainLine(string reason) =>
        _text.Get("update.keychain_unavailable").Replace("{reason}", reason, StringComparison.Ordinal);

    private async Task<string?> DownloadAsync(UpdateOffer offer)
    {
        _download = new CancellationTokenSource();
        IsDownloading = true;
        Percent = 0;
        Status = _text.Get("update.downloading").Replace("{name}", offer.Asset.Name, StringComparison.Ordinal);
        try
        {
            var download = _service.Download(offer, _folder, _download.Token);
            download.Job.ProgressChanged += progress => UiThread.Post(() => Report(progress));
            var result = await download.Job.Completion.ConfigureAwait(true);
            if (result.Outcome == JobOutcome.Cancelled)
            {
                Status = _text.Get("update.cancelled");
                return null;
            }

            if (download.RefusalKey is { } refusal)
            {
                Status = _text.Get(refusal);
                return null;
            }

            if (result.Failed.Count > 0 || download.Path is null)
            {
                Fail(result.Failed.Count > 0 ? result.Failed[0].Error : null);
                return null;
            }

            return download.Path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            Fail(OperationError.FromException(ex));
            return null;
        }
        finally
        {
            _download.Dispose();
            _download = null;
        }
    }

    private async Task InstallFileAsync(string path, string version)
    {
        Status = _text.Get("update.installing");
        var kind = _checker.Environment.Kind;
        var step = await Task.Run(() => UpdateApply.Install(kind, path, null, version)).ConfigureAwait(true);
        if (!step.IsSuccess || step.Value is null)
        {
            Status = NotWritableLine(step.Error) ?? FailLine(OperationError.FromMessage(step.Error ?? "update"));
            return;
        }

        Status = _text.Get("update.restarting");
        _restarter.Restart(step.Value);
    }

    /// <summary>The localized not-writable line for Core's English "Cannot update: {folder} is not writable. ..." error; else null.</summary>
    private string? NotWritableLine(string? error)
    {
        if (error is null || !error.StartsWith(NotWritablePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = error.IndexOf(NotWritableSuffix, NotWritablePrefix.Length, StringComparison.Ordinal);
        var folder = end < 0 ? error[NotWritablePrefix.Length..] : error[NotWritablePrefix.Length..end];
        return _text.Get("update.not_writable").Replace("{folder}", folder, StringComparison.Ordinal);
    }

    private void Report(JobProgress progress)
    {
        if (progress.BytesTotal > 0)
        {
            Percent = 100.0 * progress.BytesDone / progress.BytesTotal;
        }
    }

    private void Fail(OperationError? error) => Status = FailLine(error);

    private string FailLine(OperationError? error)
    {
        var line = _text.Get("update.failed");
        if (error is not null)
        {
            line += " " + ErrorLines.Describe(_text, error);
        }

        return line;
    }
}
