using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Ftp;
using D3skCommander.Core.Jobs;

namespace D3skCommander.App.Features.Ftp;

public sealed partial class FtpViewModel
{
    /// <summary>The marked remote entries, else the one under the cursor; never the parent row.</summary>
    internal List<Entry> SelectedRemote()
    {
        var chosen = new List<Entry>();
        foreach (var row in _rows)
        {
            if (!row.IsParent && _remote.IsMarked(row.Path))
            {
                chosen.Add(row);
            }
        }

        var index = _remote.CursorIndex;
        if (chosen.Count == 0 && index >= 0 && index < _rows.Count && !_rows[index].IsParent)
        {
            chosen.Add(_rows[index]);
        }

        return chosen;
    }

    /// <summary>Downloads the selected remote entries into the active panel's folder, as a job.</summary>
    public Task<IJob?> DownloadAsync()
    {
        var session = _session;
        var chosen = SelectedRemote();
        var capture = _panels.Capture();
        if (session is null || chosen.Count == 0 || capture is null)
        {
            return Task.FromResult<IJob?>(null);
        }

        var transfers = new FtpTransfers(session);
        var items = chosen.Select(entry => new JobItem(entry.Name)).ToList();
        var byName = chosen.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
        var done = 0;
        var job = _jobs.Start(
            _text.Get("ftp.job.download"),
            items,
            (item, progress, token) => transfers.DownloadAsync(byName[item.Name], capture.ActiveDirectory, done++, items.Count, progress, token));
        return Finish(job);
    }

    /// <summary>Uploads the active panel's selection into the remote folder on screen, as a job.</summary>
    public Task<IJob?> UploadAsync()
    {
        var session = _session;
        var capture = _panels.Capture();
        if (session is null || capture is null || capture.Sources.Count == 0)
        {
            return Task.FromResult<IJob?>(null);
        }

        var transfers = new FtpTransfers(session);
        var folder = RemotePath;
        var sources = capture.Sources.ToList();
        var items = sources.Select(path => new JobItem(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)))).ToList();
        var byName = sources.Zip(items, (path, item) => (item.Name, path)).ToDictionary(pair => pair.Name, pair => pair.path, StringComparer.Ordinal);
        var done = 0;
        var job = _jobs.Start(
            _text.Get("ftp.job.upload"),
            items,
            (item, progress, token) => transfers.UploadAsync(byName[item.Name], folder, done++, items.Count, progress, token));
        return Finish(job);
    }

    /// <summary>Waits for the job, reports a failure, then lists the remote folder and both panels again.</summary>
    private async Task<IJob?> Finish(IJob job)
    {
        Status = job.Title;
        var result = await job.Completion.ConfigureAwait(true);
        if (result.Failed.Count > 0)
        {
            Fail(result.Failed[0].Error);
        }
        else if (result.Outcome == JobOutcome.Cancelled)
        {
            Status = _text.Get("ftp.status.cancelled");
        }

        await RefreshAsync().ConfigureAwait(true);
        await _panels.RefreshAsync().ConfigureAwait(true);
        return job;
    }
}
