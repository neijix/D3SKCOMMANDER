using System.Threading.Channels;
using D3skCommander.App.Dialogs;

namespace D3skCommander.App.Tests.Dialogs;

/// <summary>Records each modal and completes it when the test answers. Opens no window.</summary>
public sealed class FakeDialogSurface : IDialogSurface
{
    private readonly Channel<DialogRequest> _shown = Channel.CreateUnbounded<DialogRequest>();
    private readonly Queue<DialogResult> _prepared = new();
    private TaskCompletionSource<DialogResult>? _pending;

    /// <summary>How long an open dialog waits for the test's answer before it fails the test.</summary>
    public static TimeSpan AnswerLimit { get; } = TimeSpan.FromSeconds(30);

    public DialogRequest? Current { get; private set; }

    public int ShownCount { get; private set; }

    public void Enqueue(DialogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (_pending is not null && !_pending.Task.IsCompleted)
        {
            _pending.TrySetResult(result);
            return;
        }

        _prepared.Enqueue(result);
    }

    public void Answer(DialogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var pending = _pending ?? throw new InvalidOperationException("No dialog is open.");
        if (!pending.TrySetResult(result))
        {
            throw new InvalidOperationException("The dialog was already answered.");
        }
    }

    public async ValueTask<DialogRequest> WaitShownAsync()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            return await _shown.Reader.ReadAsync(limit.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            throw new TimeoutException("No dialog was shown within 30 s.");
        }
    }

    public async Task<DialogResult> ShowAsync(DialogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = answer;
        Current = request;
        ShownCount++;
        if (!_shown.Writer.TryWrite(request))
        {
            throw new InvalidOperationException("Could not record the open dialog.");
        }

        if (_prepared.Count > 0)
        {
            answer.TrySetResult(_prepared.Dequeue());
        }

        using var registration = cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));
        try
        {
            // A dialog the test never answers fails the test instead of hanging the run.
            return await answer.Task.WaitAsync(AnswerLimit, CancellationToken.None);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"The dialog \"{request.Title}\" ({request.Text}) was shown but not answered within {AnswerLimit.TotalSeconds:0} s: enqueue or answer it in the test.");
        }
        finally
        {
            if (ReferenceEquals(_pending, answer))
            {
                _pending = null;
                Current = null;
            }
        }
    }
}
