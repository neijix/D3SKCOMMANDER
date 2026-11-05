using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Compare;

/// <summary>
/// Compares two files on the job service, recompares edited buffers, and saves without changing line endings.
/// <see cref="Poll"/> applies the result, so the caller does not wait.
/// </summary>
public sealed class CompareSession
{
    private readonly IJobService _jobs;
    private readonly object _gate = new();
    private int _generation;
    private IJob? _job;
    private CompareOutcome? _ready;
    private CompareBuffer[]? _sides;

    public CompareSession(string left, string right, IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(jobs);
        LeftPath = left;
        RightPath = right;
        _jobs = jobs;
    }

    public string LeftPath { get; set; }

    public string RightPath { get; set; }

    public DiffOptions Options { get; set; } = DiffOptions.Default;

    public CompareOutcome Outcome { get; private set; } = CompareOutcome.None;

    public CompareMode Mode { get; private set; } = CompareMode.View;

    public int? Current { get; private set; }

    public int? ScrollTo { get; private set; }

    public CompareStatus Status { get; private set; } = CompareStatus.Empty;

    public CompareBuffer? LeftBuffer => SideAt(0);

    public CompareBuffer? RightBuffer => SideAt(1);

    /// <summary>True until <see cref="Poll"/> takes the result of the compare that was started.</summary>
    public bool HasPendingResult
    {
        get
        {
            lock (_gate)
            {
                return _job is not null;
            }
        }
    }

    /// <summary>Raised after a side is saved, so the panels can reload.</summary>
    public event Action? ReloadRequested;

    public void CompareFiles()
    {
        _sides = null;
        Mode = CompareMode.View;
        Status = CompareStatus.Empty;
        var left = LeftPath.Trim();
        var right = RightPath.Trim();
        var options = Options;
        Start(() => FromFiles(left, right, options));
    }

    public void RecompareBuffers()
    {
        if (_sides is null)
        {
            return;
        }

        var left = _sides[0].Text;
        var right = _sides[1].Text;
        var options = Options;
        Start(() => CompareOutcome.Text(TextCompare.DiffText(left, right, options)));
    }

    public void Rerun()
    {
        if (Mode == CompareMode.Edit && _sides is not null)
        {
            RecompareBuffers();
            return;
        }

        CompareFiles();
    }

    public void Poll()
    {
        IJob? job;
        lock (_gate)
        {
            job = _job;
        }

        if (job is null || !job.Completion.IsCompleted)
        {
            return;
        }

        if (job.Completion.IsFaulted)
        {
            _ = job.Completion.Exception;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_job, job))
            {
                return;
            }

            _job = null;
            Current = null;
            ScrollTo = null;
            Outcome = _ready ?? CompareOutcome.Failed("comparison failed");
            _ready = null;
        }
    }

    public void EnterEdit()
    {
        if (_sides is null)
        {
            var left = CompareBuffer.Load(LeftPath.Trim());
            if (left.Buffer is null)
            {
                Status = left.Status;
                return;
            }

            var right = CompareBuffer.Load(RightPath.Trim());
            if (right.Buffer is null)
            {
                Status = right.Status;
                return;
            }

            _sides = [left.Buffer, right.Buffer];
        }

        Mode = CompareMode.Edit;
    }

    public void SetText(int side, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        RequireSides()[side].SetText(text);
    }

    public void Save(int side)
    {
        var buffer = RequireSides()[side];
        try
        {
            AtomicFile.Write(buffer.Path, buffer.Encode());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = CompareStatus.Error(ex.Message);
            return;
        }

        buffer.MarkClean();
        Status = CompareStatus.Saved(buffer.Path);
        ReloadRequested?.Invoke();
    }

    public void GoToDifference(bool forward)
    {
        if (Outcome.Kind != CompareKind.Text)
        {
            return;
        }

        var target = TargetRow(forward);
        if (target is int row)
        {
            Current = row;
            ScrollTo = row;
        }
    }

    public void AcknowledgeScroll() => ScrollTo = null;

    private int? TargetRow(bool forward)
    {
        var rows = Outcome.Rows;
        if (Current is null && forward && rows.Count > 0 && rows[0].Kind != RowKind.Equal)
        {
            return 0;
        }

        if (Current is null && forward)
        {
            return TextCompare.NextDifference(rows, 0);
        }

        if (Current is null)
        {
            return TextCompare.PrevDifference(rows, rows.Count);
        }

        if (forward)
        {
            return TextCompare.NextDifference(rows, Current.Value);
        }

        return TextCompare.PrevDifference(rows, Current.Value);
    }

    private CompareBuffer? SideAt(int index)
    {
        if (_sides is null)
        {
            return null;
        }

        return _sides[index];
    }

    private CompareBuffer[] RequireSides()
    {
        if (_sides is null)
        {
            throw new InvalidOperationException("Compare is not editing.");
        }

        return _sides;
    }

    private void Start(Func<CompareOutcome> produce)
    {
        var generation = Interlocked.Increment(ref _generation);
        var job = _jobs.Start("Compare", (_, _) =>
        {
            Publish(generation, Run(produce));
            return Task.CompletedTask;
        });
        lock (_gate)
        {
            if (generation == _generation)
            {
                _job = job;
            }
        }
    }

    private void Publish(int generation, CompareOutcome outcome)
    {
        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }

            _ready = outcome;
        }
    }

    private static CompareOutcome Run(Func<CompareOutcome> produce)
    {
        try
        {
            return produce();
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (string.IsNullOrEmpty(message))
            {
                message = "comparison failed";
            }

            return CompareOutcome.Failed(message);
        }
    }

    private static CompareOutcome FromFiles(string left, string right, DiffOptions options)
    {
        var diff = FileCompare.DiffFiles(left, right, options);
        if (!diff.IsText)
        {
            return CompareOutcome.Binary(diff.Identical, diff.FirstDifference, TryPeek(left, right, diff.FirstDifference));
        }

        return CompareOutcome.Text(diff.Rows);
    }

    private static BinaryPeek? TryPeek(string left, string right, long? first)
    {
        if (first is null)
        {
            return null;
        }

        try
        {
            return DiffNavigator.ReadPeek(left, right, first.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
