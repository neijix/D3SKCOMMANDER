using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly UpdateChecker _checker;
    private readonly UpdateService _service;
    private readonly IUpdateFeed _feed;
    private readonly IUpdateRestarter _restarter;
    private readonly ILocalizer _text;
    private readonly string _folder;
    private readonly Action<GitHubRelease?> _found;
    private CancellationTokenSource? _download;
    private GitHubRelease? _release;
    private ReleaseAsset? _asset;

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

    public UpdateViewModel(
        UpdateChecker checker,
        IUpdateFeed feed,
        UpdateService service,
        IUpdateRestarter restarter,
        ILocalizer text,
        string folder,
        Action<GitHubRelease?> found)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(restarter);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(found);
        _checker = checker;
        _feed = feed;
        _service = service;
        _restarter = restarter;
        _text = text;
        _folder = folder;
        _found = found;
        CurrentText = text.Get("update.current") + ": " + checker.Environment.Version;
    }

    public string? Name => "update";

    public string CurrentText { get; }

    public string NotesTitle => _text.Get("update.notes");

    public string CheckAgainText => _text.Get("update.check_again");

    public string InstallText => _text.Get("update.install");

    public string CancelText => _text.Get("update.cancel");

    public Control CreateContent() => new UpdateView(this);

    /// <summary>Reads the latest release and shows current against latest.</summary>
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

    /// <summary>Downloads the release file, checks it, installs it for this install kind and restarts.</summary>
    [RelayCommand]
    public async Task InstallAsync()
    {
        var release = _release;
        var asset = _asset;
        if (IsBusy || release is null || asset is null)
        {
            return;
        }

        IsBusy = true;
        CanInstall = false;
        try
        {
            var path = await DownloadAsync(release, asset).ConfigureAwait(true);
            if (path is not null)
            {
                await InstallFileAsync(path).ConfigureAwait(true);
            }
        }
        finally
        {
            IsDownloading = false;
            IsBusy = false;
            CanInstall = _asset is not null && _release is not null;
        }
    }

    [RelayCommand]
    public void CancelDownload() => _download?.Cancel();

    private void Show(UpdateCheck check)
    {
        _release = check.Release;
        _asset = null;
        _found(check.Outcome == UpdateOutcome.Newer ? check.Release : null);
        if (check.Outcome == UpdateOutcome.Failed)
        {
            Latest = "";
            Notes = "";
            Status = _text.Get("update.check_failed");
            return;
        }

        Latest = _text.Get("update.latest") + ": " + check.Release!.Version;
        if (check.Outcome == UpdateOutcome.UpToDate)
        {
            Notes = "";
            Status = _text.Get("update.up_to_date");
            return;
        }

        Notes = check.Release.Notes;
        Status = _text.Get("update.available").Replace("{version}", check.Release.Version, StringComparison.Ordinal);
        if (_checker.Environment.Kind is InstallKind.Source)
        {
            Status += " " + _text.Get("update.source_build");
            return;
        }

        _asset = _checker.PickAsset(check.Release);
        if (_asset is null)
        {
            Status += " " + _text.Get("update.no_package");
            return;
        }

        CanInstall = true;
    }

    private async Task<string?> DownloadAsync(GitHubRelease release, ReleaseAsset asset)
    {
        _download = new CancellationTokenSource();
        IsDownloading = true;
        Percent = 0;
        Status = _text.Get("update.downloading").Replace("{name}", asset.Name, StringComparison.Ordinal);
        try
        {
            var sums = await _feed.SumsAsync(release, _download.Token).ConfigureAwait(true);
            var download = _service.Download(asset, _folder, sums, _download.Token);
            download.Job.ProgressChanged += progress => UiThread.Post(() => Report(progress));
            var result = await download.Job.Completion.ConfigureAwait(true);
            if (result.Outcome == JobOutcome.Cancelled)
            {
                Status = _text.Get("update.cancelled");
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

    private async Task InstallFileAsync(string path)
    {
        Status = _text.Get("update.installing");
        var kind = _checker.Environment.Kind;
        var step = await Task.Run(() => UpdateApply.Install(kind, path)).ConfigureAwait(true);
        if (!step.IsSuccess || step.Value is null)
        {
            Fail(OperationError.FromMessage(step.Error ?? "update"));
            return;
        }

        Status = _text.Get("update.restarting");
        _restarter.Restart(step.Value);
    }

    private void Report(JobProgress progress)
    {
        if (progress.BytesTotal > 0)
        {
            Percent = 100.0 * progress.BytesDone / progress.BytesTotal;
        }
    }

    private void Fail(OperationError? error)
    {
        var line = _text.Get("update.failed");
        if (error is not null)
        {
            line += " " + ErrorLines.Describe(_text, error);
        }

        Status = line;
    }
}
