namespace D3skCommander.App.Tests.Smoke;

/// <summary>
/// Advances <see cref="TimeProvider"/> timers without sleeping.
/// App.Tests does not reference Microsoft.Extensions.TimeProvider.Testing, which is only pinned for Core.Tests.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utc = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private bool _draining;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utc;
        }
    }

    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        lock (_gate)
        {
            _utc += delta;
        }

        Drain();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    private void Drain()
    {
        lock (_gate)
        {
            if (_draining)
            {
                return;
            }

            _draining = true;
        }

        try
        {
            while (TryFireOne())
            {
            }
        }
        finally
        {
            lock (_gate)
            {
                _draining = false;
            }
        }
    }

    private bool TryFireOne()
    {
        ManualTimer? timer;
        lock (_gate)
        {
            timer = NextDue(_utc);
            timer?.Consume(_utc);
        }

        if (timer is null)
        {
            return false;
        }

        timer.Invoke();
        return true;
    }

    private ManualTimer? NextDue(DateTimeOffset now)
    {
        ManualTimer? found = null;
        foreach (var candidate in _timers)
        {
            if (!candidate.IsDue(now))
            {
                continue;
            }

            if (found == null || found.Due == null || candidate.Due < found.Due)
            {
                found = candidate;
            }
        }

        return found;
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly FakeTimeProvider _clock;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private bool _disposed;

        public ManualTimer(FakeTimeProvider clock, TimerCallback callback, object? state)
        {
            _clock = clock;
            _callback = callback;
            _state = state;
        }

        public DateTimeOffset? Due { get; private set; }

        public bool IsDue(DateTimeOffset now) => !_disposed && Due != null && Due.Value <= now;

        public void Consume(DateTimeOffset now)
        {
            if (Period == Timeout.InfiniteTimeSpan)
            {
                Due = null;
                return;
            }

            Due = now + Period;
        }

        public void Invoke() => _callback(_state);

        private TimeSpan Period { get; set; } = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_clock._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                Period = period;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : _clock._utc + dueTime;
            }

            _clock.Drain();
            return true;
        }

        public void Dispose()
        {
            lock (_clock._gate)
            {
                _disposed = true;
                Due = null;
            }

            _clock.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
