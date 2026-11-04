using System.Windows.Input;

namespace D3skCommander.App.Dialogs;

/// <summary>
/// The command behind a dialog button: an action and an optional guard. An async action is
/// disabled while it runs, so a second click cannot start it twice.
/// </summary>
public sealed class DialogCommand : ICommand
{
    private readonly Action? _run;
    private readonly Func<Task>? _runAsync;
    private readonly Func<bool> _canRun;
    private Task? _running;

    public DialogCommand(Action run, Func<bool>? canRun = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        _run = run;
        _canRun = canRun ?? (static () => true);
    }

    public DialogCommand(Func<Task> runAsync)
    {
        ArgumentNullException.ThrowIfNull(runAsync);
        _runAsync = runAsync;
        _canRun = static () => true;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _running is not { IsCompleted: false } && _canRun();

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        if (_run is not null)
        {
            _run();
            return;
        }

        _running = _runAsync!();
        if (!_running.IsCompleted)
        {
            NotifyCanExecuteChanged();
            _running.ContinueWith(_ => NotifyCanExecuteChanged(), TaskScheduler.Default);
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
