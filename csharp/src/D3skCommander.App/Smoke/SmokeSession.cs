using Avalonia.Controls;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Smoke;

/// <summary>Reads the screenshot variables once and arms the two delays on <see cref="TimeProvider"/>.</summary>
internal sealed class SmokeSession : IDisposable
{
    private static readonly object Gate = new();
    private static readonly List<WeakReference<SmokeSession>> Live = [];

    private readonly ILocalizer _text;
    private readonly IShellContributions _shell;
    private readonly TimeProvider _time;
    private readonly TextWriter _error;
    private readonly string? _path;
    private readonly string? _command;
    private MainWindow? _window;
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

    public static void NotifyOpened(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
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

    private void Note(MainWindow window)
    {
        if (!UiThread.CheckAccess())
        {
            return;
        }

        _window ??= window;
    }

    private ITimer Arm(TimeSpan due, Action action) =>
        _time.CreateTimer(_ => UiThread.Invoke(action), null, due, Timeout.InfiniteTimeSpan);

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
            SmokeShot.Save(Target(_window), _path);
            Report("smoke.screenshot.saved", _path);
        }
        catch (Exception ex) when (IsCaptureFailure(ex))
        {
            Report("smoke.screenshot.failed", ex.Message);
        }
    }

    /// <summary>
    /// The window the shot shows: after a command, the newest visible window it opened (an owned
    /// tool window or a modal dialog), else the main window.
    /// </summary>
    private static Window Target(MainWindow main)
    {
        for (var i = main.OwnedWindows.Count - 1; i >= 0; i--)
        {
            if (main.OwnedWindows[i] is Window { IsVisible: true } owned)
            {
                return owned;
            }
        }

        foreach (var dialog in ((Window)main).OwnedWindows.Reverse())
        {
            if (dialog.IsVisible)
            {
                return dialog;
            }
        }

        return main;
    }

    private void Report(string key, string value) =>
        _error.WriteLine(_text.Get(key).Replace("{0}", value, StringComparison.Ordinal));

    private static bool IsCaptureFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or NotSupportedException or OutOfMemoryException or PlatformNotSupportedException;

    private static void Track(SmokeSession session)
    {
        lock (Gate)
        {
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
