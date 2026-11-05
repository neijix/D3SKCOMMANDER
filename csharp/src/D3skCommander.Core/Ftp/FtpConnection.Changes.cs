using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Ftp;

public sealed partial class FtpConnection
{
    /// <summary>
    /// Creates one remote folder. An existing path is <see cref="ErrorKind.AlreadyExists"/>; a
    /// missing parent is <see cref="ErrorKind.NotFound"/>.
    /// </summary>
    public async Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
        if (turn.Error is not null)
        {
            return FileStatus.Fail(WithPath(turn.Error, path));
        }

        var existing = await EntryLockedAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing.IsSuccess)
        {
            return FileStatus.Fail(OperationError.AlreadyExists(path));
        }

        if (existing.Error!.Kind != ErrorKind.NotFound)
        {
            return FileStatus.Fail(existing.Error);
        }

        var parent = FtpPaths.Parent(path);
        if (parent.Length > 0 && parent != "/")
        {
            var inside = await EntryLockedAsync(parent, cancellationToken).ConfigureAwait(false);
            if (!inside.IsSuccess || !inside.Value.IsDirectory)
            {
                return FileStatus.Fail(OperationError.NotFound(path, "the parent folder is missing"));
            }
        }

        try
        {
            await _client.CreateDirectory(path, false, cancellationToken).ConfigureAwait(false);
            return FileStatus.Success;
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            return FileStatus.Fail(FtpErrors.Describe(ex, path));
        }
    }

    /// <summary>
    /// Renames inside the folder. <paramref name="newName"/> is a name, not a path. Returns the new
    /// path. An existing target is <see cref="ErrorKind.AlreadyExists"/> (with the new path).
    /// </summary>
    public async Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(newName);
        if (!FtpPaths.IsSafeName(newName))
        {
            return FileResult<string>.Fail(OperationError.InvalidData("not a file name: " + newName, path));
        }

        using var turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
        if (turn.Error is not null)
        {
            return FileResult<string>.Fail(WithPath(turn.Error, path));
        }

        var source = await EntryLockedAsync(path, cancellationToken).ConfigureAwait(false);
        if (!source.IsSuccess)
        {
            return FileResult<string>.Fail(source.Error!);
        }

        var target = FtpPaths.Join(FtpPaths.Parent(path), newName);
        var taken = await EntryLockedAsync(target, cancellationToken).ConfigureAwait(false);
        if (taken.IsSuccess)
        {
            return FileResult<string>.Fail(OperationError.AlreadyExists(target));
        }

        try
        {
            await _client.Rename(path, target, cancellationToken).ConfigureAwait(false);
            return FileResult<string>.Ok(target);
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            return FileResult<string>.Fail(FtpErrors.Describe(ex, path));
        }
    }

    /// <summary>Removes a file, or a folder with everything in it.</summary>
    public async Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
        if (turn.Error is not null)
        {
            return FileStatus.Fail(WithPath(turn.Error, path));
        }

        var entry = await EntryLockedAsync(path, cancellationToken).ConfigureAwait(false);
        if (!entry.IsSuccess)
        {
            return FileStatus.Fail(entry.Error!);
        }

        return entry.Value.IsDirectory
            ? await DeleteTreeAsync(path, 0, cancellationToken).ConfigureAwait(false)
            : await DeleteFileAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FileStatus> DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _client.DeleteFile(path, cancellationToken).ConfigureAwait(false);
            return FileStatus.Success;
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            return FileStatus.Fail(FtpErrors.Describe(ex, path));
        }
    }

    private async Task<FileStatus> DeleteTreeAsync(string folder, int depth, CancellationToken cancellationToken)
    {
        if (depth > MaxDeleteDepth)
        {
            return FileStatus.Fail(OperationError.InvalidData("the folder is nested too deep", folder));
        }

        var listed = await ListLockedAsync(folder, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess)
        {
            return FileStatus.Fail(listed.Error!);
        }

        foreach (var child in listed.Value)
        {
            if (!FtpPaths.IsSafeName(child.Name))
            {
                continue;
            }

            var childPath = FtpPaths.Join(folder, child.Name);
            var removed = child.IsDirectory
                ? await DeleteTreeAsync(childPath, depth + 1, cancellationToken).ConfigureAwait(false)
                : await DeleteFileAsync(childPath, cancellationToken).ConfigureAwait(false);
            if (!removed.IsSuccess)
            {
                return removed;
            }
        }

        try
        {
            await _client.DeleteDirectory(folder, cancellationToken).ConfigureAwait(false);
            return FileStatus.Success;
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            return FileStatus.Fail(FtpErrors.Describe(ex, folder));
        }
    }
}
