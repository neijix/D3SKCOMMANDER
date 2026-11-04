using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Smoke;

/// <summary>Reads the screenshot variables once and arms the two delays on <see cref="TimeProvider"/>.</summary>
internal sealed class SmokeSession : IDisposable
{
    private static readonly object Gate = new();
    private static readonly List<WeakReference<SmokeSession>> Live = [];
    private static bool _listening;

    private readonly ILocalizer _text;
    private readonly IShellContributions _shell;
    private readonly TimeProvider _time;
    private readonly TextWriter _error;
    private readonly Dispatcher _dispatcher;
    private readonly string? _path;
    private readonly string? _command;
    private Window? _window;
    private ITimer? _commandTimer;
    private ITimer? _shotTimer;
    private int _started;
    private int _commandRan;
    private int _shot;

    public SmokeSession(ILocalizer text, IShellContributions shell, TimeProvider time, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(error);
        _text = text;
        _shell = shell;
        _time = time;
        _error = error;
        _dispatcher = Dispatcher.UIThread;
        _path = Environment.GetEnvironmentVariable(SmokeModule.ScreenshotVariable);
        var command = Environment.GetEnvironmentVariable(SmokeModule.CommandVariable);
        if (!string.IsNullOrEmpty(command))
        {
            _command = command;
        }

        IsActive = _path is not null;
        if (IsActive)
        {
            Track(this);
        }
    }

    public bool IsActive { get; }

    public void Start()
    {
        if (!IsActive || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        if (_command is not null)
        {
            _commandTimer = Arm(SmokeModule.CommandDelay, RunCommand);
        }

        _shotTimer = Arm(SmokeModule.ShotDelay, Capture);
    }

    public void Dispose()
    {
        Untrack(this);
        _commandTimer?.Dispose();
        _shotTimer?.Dispose();
        _commandTimer = null;
        _shotTimer = null;
    }

    internal void Note(Window window)
    {
        if (!ReferenceEquals(_dispatcher, Dispatcher.UIThread))
        {
            return;
        }

        _window ??= window;
    }

    private ITimer Arm(TimeSpan due, Action action) =>
        _time.CreateTimer(_ => OnUi(action), null, due, Timeout.InfiniteTimeSpan);

    private void RunCommand()
    {
        if (_command is null || Interlocked.Exchange(ref _commandRan, 1) != 0)
        {
            return;
        }

        var command = CommandCatalog.FromName(_command);
        if (command is null)
        {
            Report("smoke.command.unknown", _command);
            return;
        }

        _shell.Dispatch(command.Value);
    }

    private void Capture()
    {
        if (Interlocked.Exchange(ref _shot, 1) != 0)
        {
            return;
        }

        try
        {
            Shoot();
        }
        finally
        {
            _window?.Close();
        }
    }

    private void Shoot()
    {
        if (_window is null || _path is null)
        {
            Report("smoke.screenshot.failed", _text.Get("smoke.window.missing"));
            return;
        }

        try
        {
            SmokeShot.Save(_window, _path);
            Report("smoke.screenshot.saved", _path);
        }
        catch (Exception ex) when (IsCaptureFailure(ex))
        {
            Report("smoke.screenshot.failed", ex.Message);
        }
    }

    private void Report(string key, string value) =>
        _error.WriteLine(_text.Get(key).Replace("{0}", value, StringComparison.Ordinal));

    private static bool IsCaptureFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or NotSupportedException or OutOfMemoryException or PlatformNotSupportedException;

    private static void OnUi(Action action)
    {
        var dispatcher = Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private static void Track(SmokeSession session)
    {
        lock (Gate)
        {
            EnsureListening();
            Live.Add(new WeakReference<SmokeSession>(session));
        }
    }

    private static void Untrack(SmokeSession session)
    {
        lock (Gate)
        {
            for (var i = Live.Count - 1; i >= 0; i--)
            {
                if (!Live[i].TryGetTarget(out var alive) || ReferenceEquals(alive, session))
                {
                    Live.RemoveAt(i);
                }
            }
        }
    }

    private static void EnsureListening()
    {
        if (_listening)
        {
            return;
        }

        _listening = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>(
            OnWindowOpened,
            RoutingStrategies.Direct | RoutingStrategies.Bubble,
            true);
    }

    private static void OnWindowOpened(Window window, RoutedEventArgs args)
    {
        if (window is not MainWindow)
        {
            return;
        }

        List<SmokeSession> sessions;
        lock (Gate)
        {
            sessions = LiveSessions();
        }

        foreach (var session in sessions)
        {
            session.Note(window);
        }
    }

    private static List<SmokeSession> LiveSessions()
    {
        var sessions = new List<SmokeSession>();
        for (var i = Live.Count - 1; i >= 0; i--)
        {
            if (!Live[i].TryGetTarget(out var session))
            {
                Live.RemoveAt(i);
                continue;
            }

            sessions.Add(session);
        }

        return sessions;
    }
}
