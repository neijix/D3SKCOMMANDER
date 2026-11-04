using System.Runtime.CompilerServices;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

public sealed partial class LocalFileSystemProvider
{
    protected override async IAsyncEnumerable<FileResult<Entry>> ListCore(
        string directory,
        bool showHidden,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // CompletedTask keeps this an async iterator without a thread hop per entry.
        await Task.CompletedTask;
        if (cancellationToken.IsCancellationRequested)
        {
            yield return FileResult<Entry>.Fail(OperationError.Cancelled(directory));
            yield break;
        }

        // Row 8 reads the folder; entries are then handed out one at a time so cancel stops the rest.
        var listed = LocalFileSystem.ListDirectory(directory, showHidden);
        if (!listed.IsSuccess)
        {
            yield return FileResult<Entry>.Fail(listed.Error ?? OperationError.NotFound(directory));
            yield break;
        }

        foreach (var entry in listed.Value)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return FileResult<Entry>.Fail(OperationError.Cancelled(directory));
                yield break;
            }

            yield return FileResult<Entry>.Ok(entry);
        }
    }

    protected override Task<FileResult<Entry>> GetEntryCore(string path, CancellationToken cancellationToken)
    {
        if (Cancelled(cancellationToken, path, out var error))
        {
            return Task.FromResult(FileResult<Entry>.Fail(error!));
        }

        return Task.FromResult(LocalFileSystem.ReadEntry(path));
    }
}
