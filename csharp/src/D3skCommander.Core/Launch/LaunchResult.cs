using D3skCommander.Core.Common;

namespace D3skCommander.Core.Launch;

/// <summary>Why nothing was started on purpose; the app turns it into a localized text.</summary>
public enum LaunchRefusal
{
    /// <summary>The folder name holds a character (% ! or a quote) that an elevated cmd or Windows Terminal line cannot carry safely.</summary>
    UnsafeFolderName,

    /// <summary>PowerShell 7 (<c>pwsh</c>) is not on PATH where it is required (Linux).</summary>
    PowerShellNotFound,

    /// <summary>No terminal emulator started.</summary>
    NoTerminalFound,

    /// <summary>The program a terminal setting starts is not on PATH (Windows).</summary>
    ProgramNotFound,
}

/// <summary>
/// How many processes started, or the typed error from the one that did not. Two outcomes are neither:
/// <see cref="Cancelled"/> (the user declined the UAC prompt) and <see cref="Refused"/> (nothing was
/// started on purpose); both have a null <see cref="Error"/> and are not <see cref="IsSuccess"/>.
/// </summary>
public sealed class LaunchResult
{
    private LaunchResult(int started, OperationError? error, bool isCancelled = false, LaunchRefusal? refusal = null)
    {
        Started = started;
        Error = error;
        IsCancelled = isCancelled;
        Refusal = refusal;
    }

    public int Started { get; }

    public OperationError? Error { get; }

    public bool IsSuccess => Error is null && !IsCancelled && !IsRefused;

    /// <summary>The user declined the elevation prompt (Win32 error 1223).</summary>
    public bool IsCancelled { get; }

    /// <summary>Nothing was started on purpose (an unsafe request, a missing program or terminal); <see cref="Refusal"/> is the typed reason the UI shows as a status.</summary>
    public bool IsRefused => Refusal is not null;

    public LaunchRefusal? Refusal { get; }

    public static LaunchResult Cancelled { get; } = new(0, null, isCancelled: true);

    public static LaunchResult Ok(int started)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(started);
        return new LaunchResult(started, null);
    }

    public static LaunchResult Fail(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new LaunchResult(0, error);
    }

    public static LaunchResult Refused(LaunchRefusal reason)
    {
        return new LaunchResult(0, null, refusal: reason);
    }
}
