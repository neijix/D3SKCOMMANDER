using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Splitter;

/// <summary>A split or combine queued on the job service. The result fields are set before <see cref="Job"/> completes.</summary>
public abstract class SplitterRun : IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private int _released;

    internal SplitterRun()
    {
    }

    public IJob Job { get; private set; } = null!;

    public OperationError? Error { get; private set; }

    internal void Bind(IJob job)
    {
        Job = job;
        _ready.Set();
    }

    internal void WaitUntilBound() => _ready.Wait();

    internal void SetError(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    internal void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _ready.Dispose();
        }
    }

    public void Dispose() => Release();
}
