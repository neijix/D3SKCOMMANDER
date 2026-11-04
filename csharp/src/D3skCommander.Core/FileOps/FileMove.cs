using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.FileOps;

/// <summary>
/// A move renames on the same volume. <see cref="IFileSystemProvider.RenameAsync"/> stays in one
/// folder, so a local move uses the OS rename. Across volumes, or between providers, it copies
/// and then deletes.
/// </summary>
internal static class FileMove
{
    public static async Task TransferAsync(
        TransferWork work,
        string source,
        string target,
        TreeCount.Totals count,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var entry = await work.Source.GetEntryAsync(source, cancellationToken).ConfigureAwait(false);
        if (!entry.IsSuccess)
        {
            if (entry.Error is not null && entry.Error.Kind == ErrorKind.Cancelled)
            {
                work.Abort();
            }

            work.Fail(source, target, entry.Error ?? OperationError.NotFound(source), progress, count.Files, count.Bytes, true);
            return;
        }

        var refusal = Refuse(source, target, entry.Value);
        if (refusal is not null)
        {
            work.Fail(source, target, refusal, progress, count.Files, count.Bytes, true);
            return;
        }

        var directory = entry.Value.IsDirectory && !entry.Value.IsLink;
        var absent = !await work.ExistsAsync(target, cancellationToken).ConfigureAwait(false);
        if (work.Moving && absent && TryRename(work, source, target, directory, false))
        {
            work.Succeed(source, target, count.Files, count.Bytes, progress);
            return;
        }

        await FileCopy.CopyTreeAsync(work, source, target, progress, cancellationToken).ConfigureAwait(false);
    }

    public static bool TryRename(TransferWork work, string from, string to, bool directory, bool overwrite)
    {
        if (!LocalMetadata.BothLocal(work.Source, work.Destination))
        {
            return false;
        }

        try
        {
            if (directory)
            {
                Directory.Move(from, to);
            }
            else
            {
                File.Move(from, to, overwrite);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static async Task RemoveIfEmptyAsync(
        IFileSystemProvider provider,
        string path,
        CancellationToken cancellationToken)
    {
        await foreach (var child in provider.ListAsync(path, true, cancellationToken).ConfigureAwait(false))
        {
            if (!child.IsSuccess || !TreeCount.IsParent(child.Value))
            {
                return;
            }
        }

        await provider.DeleteAsync(path, CancellationToken.None).ConfigureAwait(false);
    }

    private static OperationError? Refuse(string source, string target, Entry entry)
    {
        var resolved = FileOpNames.Full(source);
        var fullTarget = FileOpNames.Full(target);
        if (string.Equals(resolved, fullTarget, FileOpNames.PathComparison))
        {
            return OperationError.Other("source and destination are the same", source);
        }

        if (entry.IsDirectory && !entry.IsLink && FileOpNames.IsInside(resolved, fullTarget))
        {
            return OperationError.Other("cannot copy a folder into itself", source);
        }

        return null;
    }
}
