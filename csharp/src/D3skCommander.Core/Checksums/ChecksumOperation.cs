using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Checksums;

/// <summary>A checksum create or verify queued on the job service. Read the counts after <see cref="Job"/> completes.</summary>
public sealed class ChecksumOperation
{
    private readonly object _gate = new();
    private readonly List<ChecksumVerdict> _verdicts = [];
    private IJob? _job;
    private int _linesWritten;

    internal ChecksumOperation()
    {
    }

    public IJob Job => _job ?? throw new InvalidOperationException("The checksum job is not started.");

    public int LinesWritten
    {
        get
        {
            lock (_gate)
            {
                return _linesWritten;
            }
        }
    }

    public IReadOnlyList<ChecksumVerdict> Verdicts
    {
        get
        {
            lock (_gate)
            {
                return _verdicts.ToArray();
            }
        }
    }

    internal void Attach(IJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        _job = job;
    }

    internal void Wrote(int lines)
    {
        lock (_gate)
        {
            _linesWritten += lines;
        }
    }

    internal void Add(ChecksumVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        lock (_gate)
        {
            _verdicts.Add(verdict);
        }
    }
}
