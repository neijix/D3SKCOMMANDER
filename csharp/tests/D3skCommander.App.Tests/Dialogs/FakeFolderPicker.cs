using D3skCommander.App.Dialogs;

namespace D3skCommander.App.Tests.Dialogs;

/// <summary>Answers a folder pick without opening the OS chooser.</summary>
public sealed class FakeFolderPicker : IFolderPicker
{
    private readonly Queue<string?> _ready = new();
    private readonly Queue<TaskCompletionSource<string?>> _waiting = new();

    public int Calls { get; private set; }

    public string? LastStart { get; private set; }

    public void Enqueue(string? path) => Answer(path);

    public void Answer(string? path)
    {
        if (_waiting.Count == 0)
        {
            _ready.Enqueue(path);
            return;
        }

        _waiting.Dequeue().TrySetResult(path);
    }

    public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastStart = startFolder;
        cancellationToken.ThrowIfCancellationRequested();
        if (_ready.Count > 0)
        {
            return Task.FromResult(_ready.Dequeue());
        }

        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting.Enqueue(pending);
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
        return pending.Task;
    }
}
