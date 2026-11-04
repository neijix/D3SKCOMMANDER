using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.FileOps;

/// <summary>Copies a tree. An overwrite truncates the target in place; a failed write removes that partial.</summary>
internal static partial class FileCopy
{
    public const int BufferSize = 1024 * 1024;

    public static async Task CopyTreeAsync(
        TransferWork work,
        string source,
        string target,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            work.Abort();
        }

        var entry = await work.Source.GetEntryAsync(source, cancellationToken).ConfigureAwait(false);
        if (!entry.IsSuccess)
        {
            RefuseMissing(work, source, target, entry.Error, progress);
            return;
        }

        if (entry.Value.IsLink && CanReplicateLink(work))
        {
            await CopyLinkAsync(work, source, entry.Value, target, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (entry.Value.IsDirectory)
        {
            await CopyDirectoryAsync(work, source, target, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        await CopyFileAsync(work, source, entry.Value, target, progress, cancellationToken).ConfigureAwait(false);
    }

    private static void RefuseMissing(
        TransferWork work,
        string source,
        string target,
        OperationError? error,
        IJobProgress progress)
    {
        if (error is not null && error.Kind == ErrorKind.Cancelled)
        {
            work.Abort();
        }

        work.Fail(source, target, error ?? OperationError.NotFound(source), progress, 1, 0, true);
    }

    private static bool CanReplicateLink(TransferWork work) =>
        !OperatingSystem.IsWindows() && LocalMetadata.BothLocal(work.Source, work.Destination);

    private static async Task CopyDirectoryAsync(
        TransferWork work,
        string source,
        string target,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var created = await PrepareDirectoryAsync(work, source, target, progress, cancellationToken).ConfigureAwait(false);
        if (created is null)
        {
            return;
        }

        var children = await ReadChildrenAsync(work, source, cancellationToken).ConfigureAwait(false);
        if (children is null)
        {
            work.Fail(source, target, OperationError.Other("the folder could not be read", source), progress, 0, 0, false);
            return;
        }

        await CopyChildrenAsync(work, target, children, progress, cancellationToken).ConfigureAwait(false);
        if (created.Value)
        {
            LocalMetadata.Apply(work.Source, source, work.Destination, target);
        }

        if (work.Moving)
        {
            await FileMove.RemoveIfEmptyAsync(work.Source, source, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CopyChildrenAsync(
        TransferWork work,
        string target,
        Entry[] children,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        foreach (var child in children)
        {
            var name = ChildName(child);
            await CopyTreeAsync(work, child.Path, FileOpNames.Join(target, name), progress, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<bool?> PrepareDirectoryAsync(
        TransferWork work,
        string source,
        string target,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var existing = await work.Destination.GetEntryAsync(target, cancellationToken).ConfigureAwait(false);
        if (existing.IsSuccess)
        {
            if (existing.Value.IsDirectory && !existing.Value.IsLink)
            {
                return false;
            }

            work.Fail(
                source,
                target,
                OperationError.Other("the target exists and is not a folder", source),
                progress,
                0,
                0,
                false);
            return null;
        }

        return await CreateDirectoryAsync(work, source, target, existing.Error, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool?> CreateDirectoryAsync(
        TransferWork work,
        string source,
        string target,
        OperationError? existingError,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        if (existingError is not null && existingError.Kind == ErrorKind.Cancelled)
        {
            work.Abort();
        }

        if (existingError is not null && existingError.Kind != ErrorKind.NotFound)
        {
            work.Fail(source, target, existingError, progress, 0, 0, false);
            return null;
        }

        var created = await work.Destination.CreateFolderAsync(target, cancellationToken).ConfigureAwait(false);
        if (created.IsSuccess)
        {
            return true;
        }

        if (created.Error is not null && created.Error.Kind == ErrorKind.Cancelled)
        {
            work.Abort();
        }

        work.Fail(
            source,
            target,
            created.Error ?? OperationError.Other("the folder could not be created", target),
            progress,
            0,
            0,
            false);
        return null;
    }

    private static async Task<Entry[]?> ReadChildrenAsync(
        TransferWork work,
        string directory,
        CancellationToken cancellationToken)
    {
        var children = new List<Entry>();
        await foreach (var item in work.Source.ListAsync(directory, true, cancellationToken).ConfigureAwait(false))
        {
            if (!item.IsSuccess)
            {
                if (item.Error is not null && item.Error.Kind == ErrorKind.Cancelled)
                {
                    work.Abort();
                }

                return null;
            }

            if (!TreeCount.IsParent(item.Value))
            {
                children.Add(item.Value);
            }
        }

        return children.ToArray();
    }

    private static string ChildName(Entry entry)
    {
        if (!entry.Name.Contains('/') && !entry.Name.Contains('\\'))
        {
            return entry.Name;
        }

        return Path.GetFileName(entry.Path);
    }
}
