using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

/// <summary>
/// Shared cancel and read-only checks. Subclasses implement the cores;
/// these public methods are not virtual, so a core is not called once a check has refused.
/// Cancel wins over the read-only flag. A read-only provider then refuses without touching the store.
/// </summary>
public abstract class FileSystemProvider : IFileSystemProvider
{
    /// <summary>English detail of a mutation a read-only provider refuses. The kind is <see cref="ErrorKind.Unsupported"/>.</summary>
    public const string ReadOnlyMessage = "the provider is read-only";

    public abstract bool IsReadOnly { get; }

    public IAsyncEnumerable<FileResult<Entry>> ListAsync(
        string directory,
        bool showHidden,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (cancellationToken.IsCancellationRequested)
        {
            return One(OperationError.Cancelled(directory));
        }

        return ListCore(directory, showHidden, cancellationToken);
    }

    public Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken)
    {
        var blocked = Blocked(path, cancellationToken, mutation: false);
        if (blocked is not null)
        {
            return Task.FromResult(FileResult<Entry>.Fail(blocked));
        }

        return GetEntryCore(path, cancellationToken);
    }

    public Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        var blocked = Blocked(path, cancellationToken, mutation: false);
        if (blocked is not null)
        {
            return Task.FromResult(FileResult<Stream>.Fail(blocked));
        }

        return OpenReadCore(path, cancellationToken);
    }

    public Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken)
    {
        var blocked = Blocked(path, cancellationToken, mutation: true);
        if (blocked is not null)
        {
            return Task.FromResult(FileResult<Stream>.Fail(blocked));
        }

        return OpenWriteCore(path, cancellationToken);
    }

    public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken)
    {
        var blocked = Blocked(path, cancellationToken, mutation: true);
        if (blocked is not null)
        {
            return Task.FromResult(FileStatus.Fail(blocked));
        }

        return CreateFolderCore(path, cancellationToken);
    }

    public Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newName);
        var blocked = Blocked(path, cancellationToken, mutation: true);
        if (blocked is not null)
        {
            return Task.FromResult(FileResult<string>.Fail(blocked));
        }

        return RenameCore(path, newName, cancellationToken);
    }

    public Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        var blocked = Blocked(path, cancellationToken, mutation: true);
        if (blocked is not null)
        {
            return Task.FromResult(FileStatus.Fail(blocked));
        }

        return DeleteCore(path, cancellationToken);
    }

    protected abstract IAsyncEnumerable<FileResult<Entry>> ListCore(
        string directory,
        bool showHidden,
        CancellationToken cancellationToken);

    protected abstract Task<FileResult<Entry>> GetEntryCore(string path, CancellationToken cancellationToken);

    protected abstract Task<FileResult<Stream>> OpenReadCore(string path, CancellationToken cancellationToken);

    protected abstract Task<FileResult<Stream>> OpenWriteCore(string path, CancellationToken cancellationToken);

    protected abstract Task<FileStatus> CreateFolderCore(string path, CancellationToken cancellationToken);

    protected abstract Task<FileResult<string>> RenameCore(
        string path,
        string newName,
        CancellationToken cancellationToken);

    protected abstract Task<FileStatus> DeleteCore(string path, CancellationToken cancellationToken);

    private OperationError? Blocked(string path, CancellationToken cancellationToken, bool mutation)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (cancellationToken.IsCancellationRequested)
        {
            return OperationError.Cancelled(path);
        }

        if (mutation && IsReadOnly)
        {
            return OperationError.Unsupported(ReadOnlyMessage, path);
        }

        return null;
    }

    private static async IAsyncEnumerable<FileResult<Entry>> One(OperationError error)
    {
        await Task.CompletedTask;
        yield return FileResult<Entry>.Fail(error);
    }
}
