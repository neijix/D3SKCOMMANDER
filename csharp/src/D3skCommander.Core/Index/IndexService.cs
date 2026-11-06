namespace D3skCommander.Core.Index;

/// <summary>
/// The one name index of the app: loads the saved file, builds a new one on a worker (cancellable, with a running
/// count of entries) and saves it. <see cref="Changed"/> fires on whatever thread did the work; readers keep
/// using the previous index until the new one replaces it.
/// </summary>
public sealed class IndexService : IDisposable
{
    private readonly string _file;
    private readonly Func<IndexOptions> _options;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private NameIndex? _current;
    private Task _build = Task.CompletedTask;
    private CancellationTokenSource? _cancel;
    private long _progress;
    private bool _building;
    private bool _wasCancelled;

    public IndexService(string file, Func<IndexOptions> options, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        ArgumentNullException.ThrowIfNull(options);
        _file = file;
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised when the index, the progress or the building state changed. Any thread.</summary>
    public event Action? Changed;

    public string File => _file;

    public NameIndex? Current => Volatile.Read(ref _current);

    public bool IsBuilding => Volatile.Read(ref _building);

    /// <summary>Entries found by the build in progress.</summary>
    public long Progress => Interlocked.Read(ref _progress);

    /// <summary>True when the last build was cancelled before it finished.</summary>
    public bool WasCancelled => Volatile.Read(ref _wasCancelled);

    /// <summary>Completes when the build in progress (if any) has finished, failed or been cancelled.</summary>
    public Task Completion => Volatile.Read(ref _build);

    public bool HasFile => System.IO.File.Exists(_file);

    /// <summary>When the saved file was built, or null when there is none.</summary>
    public DateTimeOffset? SavedAt => NameIndexFile.ReadBuiltAt(_file);

    /// <summary>True when the saved file is older than <paramref name="hours"/> (see <see cref="IndexRefresh"/>).</summary>
    public bool IsDue(int hours) => IndexRefresh.IsDue(SavedAt, _time.GetUtcNow(), hours);

    /// <summary>Reads the saved file into <see cref="Current"/> unless an index is already loaded. False when there is none to read.</summary>
    public async Task<bool> LoadAsync()
    {
        if (Current is not null)
        {
            return true;
        }

        var loaded = await Task.Run(() => NameIndexFile.Load(_file)).ConfigureAwait(false);
        if (loaded is null)
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _current, loaded, null) is null)
        {
            Changed?.Invoke();
        }

        return true;
    }

    /// <summary>Starts a build, or returns the one running. The old index serves queries until the new one is saved.</summary>
    public Task BuildAsync()
    {
        lock (_gate)
        {
            if (!_build.IsCompleted)
            {
                return _build;
            }

            _cancel?.Dispose();
            var cancel = new CancellationTokenSource();
            _cancel = cancel;
            var options = _options();
            Volatile.Write(ref _building, true);
            Volatile.Write(ref _wasCancelled, false);
            Interlocked.Exchange(ref _progress, 0);
            _build = Task.Run(() => Run(options, cancel.Token));
        }

        Changed?.Invoke();
        return _build;
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _cancel?.Cancel();
        }
    }

    public void Dispose()
    {
        Cancel();
        lock (_gate)
        {
            _cancel?.Dispose();
            _cancel = null;
        }
    }

    private void Run(IndexOptions options, CancellationToken cancel)
    {
        try
        {
            var built = IndexScanner.Build(options, new Counter(this), cancel, _time);
            NameIndexFile.Save(built, _file);
            Volatile.Write(ref _current, built);
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref _wasCancelled, true);
        }
        finally
        {
            Volatile.Write(ref _building, false);
            Changed?.Invoke();
        }
    }

    private sealed class Counter(IndexService owner) : IProgress<long>
    {
        public void Report(long value)
        {
            Interlocked.Exchange(ref owner._progress, value);
            owner.Changed?.Invoke();
        }
    }
}
