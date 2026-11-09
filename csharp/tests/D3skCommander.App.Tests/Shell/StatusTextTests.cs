using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

public class StatusTextTests
{
    [Fact]
    public void AStatusTextClearsOnTheNextDispatchedCommand()
    {
        var shell = new ShellContributions(new ManualTime());
        var seen = new List<string?>();
        shell.StatusChanged += seen.Add;
        shell.Handle(CommandId.Refresh, () => { });

        shell.SetStatus("hello");
        Assert.Equal("hello", shell.Status);
        shell.Dispatch(CommandId.Refresh);

        Assert.Null(shell.Status);
        Assert.Equal(["hello", null], seen);
    }

    [Fact]
    public void AStatusTextClearsAfterFiveSecondsWhenNothingRuns()
    {
        var time = new ManualTime();
        var shell = new ShellContributions(time);
        shell.SetStatus("hello");

        time.Advance(TimeSpan.FromSeconds(4.9));
        Assert.Equal("hello", shell.Status);
        time.Advance(TimeSpan.FromSeconds(0.2));

        Assert.Null(shell.Status);
    }

    [Fact]
    public void ANewTextRestartsTheFiveSeconds()
    {
        var time = new ManualTime();
        var shell = new ShellContributions(time);
        shell.SetStatus("one");
        time.Advance(TimeSpan.FromSeconds(3));
        shell.SetStatus("two");

        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal("two", shell.Status);
        time.Advance(TimeSpan.FromSeconds(2.1));
        Assert.Null(shell.Status);
    }

    [Fact]
    public void ACommandThatSetsAStatusKeepsItUntilTheNextCommand()
    {
        var shell = new ShellContributions(new ManualTime());
        shell.Handle(CommandId.Refresh, () => shell.SetStatus("from the handler"));

        shell.Dispatch(CommandId.Refresh);

        Assert.Equal("from the handler", shell.Status);
    }

    /// <summary>A clock that moves only when told; its timers fire in order as it passes their due time.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private TimeSpan _now = TimeSpan.Zero;

        public void Advance(TimeSpan by)
        {
            _now += by;
            foreach (var timer in _timers.ToArray())
            {
                timer.FireIfDue(_now);
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, _now + dueTime);
            _timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan? _due;

        public ManualTimer(TimerCallback callback, object? state, TimeSpan due)
        {
            _callback = callback;
            _state = state;
            _due = due;
        }

        public void FireIfDue(TimeSpan now)
        {
            if (_due is { } due && due <= now)
            {
                _due = null;
                _callback(_state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => _due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
