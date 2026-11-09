using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Sync;

namespace D3skCommander.App.Features.Sync;

public sealed partial class SyncViewModel
{
    private IJob? _scan;
    private SyncOperation? _apply;
    private FileScan? _scanned;

    /// <summary>The job that scans, or null before the first Compare. For tests.</summary>
    public IJob? ScanJob => _scan;

    /// <summary>The job that runs the plan, or null before the first Run. For tests.</summary>
    public IJob? ApplyJob => _apply?.Job;

    /// <summary>Starts the scan of both roots as a job.</summary>
    [RelayCommand]
    public void Compare()
    {
        if (IsBusy)
        {
            return;
        }

        if (RefuseComputer())
        {
            return;
        }

        var left = LeftPath;
        var right = RightPath;
        var options = SyncChoices.Options(Mode, Criteria, EmptyDirs);
        var scanned = new FileScan();
        _scanned = scanned;
        Failures.Clear();
        OnPropertyChanged(nameof(HasFailures));
        ClearPlan();
        _scan = _jobs.Start(_text.Get("sync.job.compare"), (_, cancellationToken) =>
        {
            scanned.Result = DirectorySync.Compare(left, right, options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        StatusLine = _text.Get("sync.scanning");
        SetBusy(true);
    }

    /// <summary>Runs the reviewed plan as a job.</summary>
    [RelayCommand]
    public void Run()
    {
        if (IsBusy || !CanRun)
        {
            return;
        }

        if (RefuseComputer())
        {
            return;
        }

        Failures.Clear();
        OnPropertyChanged(nameof(HasFailures));
        _apply = new SyncService(_jobs).Apply(LeftPath, RightPath, _items, _text.Get("sync.job.run"));
        StatusLine = _text.Get("sync.running");
        SetBusy(true);
    }

    /// <summary>Shows the read-only line and answers true when either side is Computer or under it.</summary>
    private bool RefuseComputer()
    {
        if (!FileOpGuard.IsTargetRefused(LeftPath) && !FileOpGuard.IsTargetRefused(RightPath))
        {
            return false;
        }

        StatusLine = _text.Get("computer.readonly");
        return true;
    }

    /// <summary>Cancels the scan or the run in progress.</summary>
    [RelayCommand]
    public void Cancel()
    {
        _scan?.Cancel();
        _apply?.Job.Cancel();
    }

    /// <summary>Picks up job progress and ends. The view calls this on a timer.</summary>
    public void Refresh()
    {
        if (_scan is not null && _scan.Completion.IsCompleted)
        {
            var job = _scan;
            _scan = null;
            FinishScan(job.Completion.Result);
        }

        if (_apply is not null)
        {
            RefreshApply(_apply);
        }
    }

    private void RefreshApply(SyncOperation apply)
    {
        var job = apply.Job;
        if (!job.Completion.IsCompleted)
        {
            var progress = job.Progress;
            StatusLine = Fill(
                "sync.progress",
                ("done", progress.ItemsDone.ToString(CultureInfo.InvariantCulture)),
                ("total", progress.ItemsTotal.ToString(CultureInfo.InvariantCulture)),
                ("name", progress.CurrentName));
            return;
        }

        _apply = null;
        FinishApply(apply, job.Completion.Result);
    }

    private void FinishScan(JobResult result)
    {
        var scanned = _scanned;
        SetBusy(false);
        if (result.Outcome == JobOutcome.Cancelled)
        {
            StatusLine = _text.Get("sync.cancelled");
            return;
        }

        if (result.Failed.Count > 0)
        {
            StatusLine = ErrorLines.Describe(_text, result.Failed[0].Error);
            return;
        }

        var found = scanned?.Result;
        if (found is null || !found.Value.IsSuccess)
        {
            StatusLine = found is null ? "" : ErrorLines.Describe(_text, found.Value.Error!);
            return;
        }

        var items = found.Value.Value;
        SyncChoices.ApplyMode(items, Mode);
        _items.AddRange(items);
        foreach (var item in items)
        {
            _all.Add(new SyncRow(item, _text, UpdateCounts));
        }

        Rebuild();
        StatusLine = Fill("sync.found", ("n", _items.Count.ToString(CultureInfo.InvariantCulture)));
    }

    private void FinishApply(SyncOperation apply, JobResult result)
    {
        SetBusy(false);
        var report = apply.Report;
        foreach (var error in report.Errors)
        {
            Failures.Add(ErrorLines.Describe(_text, error));
        }

        foreach (var failed in result.Failed)
        {
            Failures.Add(ErrorLines.Describe(_text, failed.Error));
        }

        OnPropertyChanged(nameof(HasFailures));
        var key = result.Outcome == JobOutcome.Cancelled ? "sync.run_cancelled" : "sync.done";
        StatusLine = Fill(
            key,
            ("copied", report.Copied.ToString(CultureInfo.InvariantCulture)),
            ("deleted", report.Deleted.ToString(CultureInfo.InvariantCulture)),
            ("errors", Failures.Count.ToString(CultureInfo.InvariantCulture)));
        ClearPlan();
        _applied?.Invoke();
    }

    private void ClearPlan()
    {
        _all.Clear();
        _items.Clear();
        Rows.Clear();
        UpdateCounts();
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        CanRun = !busy && SyncPlan.PendingCount(_items) > 0;
    }

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var row in _all)
        {
            if (Filter().Shows(row.Item.Status))
            {
                Rows.Add(row);
            }
        }

        UpdateCounts();
    }

    private SyncFilter Filter() => new()
    {
        Equal = ShowEqual,
        Newer = ShowNewer,
        LeftOnly = ShowLeftOnly,
        RightOnly = ShowRightOnly,
    };

    private void UpdateCounts()
    {
        var counts = SyncPlan.CountClasses(_items);
        EqualText = Fill("sync.filter.equal", ("n", counts[0].ToString(CultureInfo.InvariantCulture)));
        NewerText = Fill("sync.filter.newer", ("n", counts[1].ToString(CultureInfo.InvariantCulture)));
        LeftOnlyText = Fill("sync.filter.left_only", ("n", counts[2].ToString(CultureInfo.InvariantCulture)));
        RightOnlyText = Fill("sync.filter.right_only", ("n", counts[3].ToString(CultureInfo.InvariantCulture)));
        var pending = SyncPlan.PendingCount(_items);
        PendingLine = Fill("sync.pending", ("n", pending.ToString(CultureInfo.InvariantCulture)));
        CountsLine = Fill("sync.shown", ("n", Rows.Count.ToString(CultureInfo.InvariantCulture)));
        CanRun = !IsBusy && pending > 0;
    }

    /// <summary>Where the scan job leaves its result for the model to read once the job ends.</summary>
    private sealed class FileScan
    {
        public FileResult<IReadOnlyList<SyncItem>>? Result { get; set; }
    }
}
