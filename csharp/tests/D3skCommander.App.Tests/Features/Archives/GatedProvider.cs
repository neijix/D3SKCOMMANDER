using System.Runtime.CompilerServices;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.App.Tests.Features.Archives;

/// <summary>The local disk, whose listing can be held open: a folder reload that is still running.</summary>
internal sealed class GatedProvider : IFileSystemProvider
{
    private readonly LocalFileSystemProvider _disk = new();
    private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _gate;
    private TaskCompletionSource? _held;

    public bool IsReadOnly => false;

    /// <summary>The next listing waits in its first step until <see cref="Release"/>.</summary>
    public Task HoldNextListing()
    {
        _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _entered.Task;
    }

    public void Release() => (_held ?? _gate)?.TrySetResult();

    public async IAsyncEnumerable<FileResult<Entry>> ListAsync(
        string directory,
        bool showHidden,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var gate = Interlocked.Exchange(ref _gate, null);
        if (gate is not null)
        {
            _held = gate;
            _entered.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await foreach (var item in _disk.ListAsync(directory, showHidden, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken) =>
        _disk.GetEntryAsync(path, cancellationToken);

    public Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken) =>
        _disk.OpenReadAsync(path, cancellationToken);

    public Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken) =>
        _disk.OpenWriteAsync(path, cancellationToken);

    public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
        _disk.CreateFolderAsync(path, cancellationToken);

    public Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken) =>
        _disk.RenameAsync(path, newName, cancellationToken);

    public Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken) =>
        _disk.DeleteAsync(path, cancellationToken);
}
