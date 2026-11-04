using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.FileOps;

/// <summary>Files and bytes under a path. Symlinks count as one file and are not followed.</summary>
internal static class TreeCount
{
    public readonly struct Totals
    {
        public Totals(int files, long bytes)
        {
            Files = files;
            Bytes = bytes;
        }

        public int Files { get; }

        public long Bytes { get; }
    }

    public static async Task<Totals> CountAsync(
        IFileSystemProvider provider,
        string path,
        CancellationToken cancellationToken)
    {
        var entry = await provider.GetEntryAsync(path, cancellationToken).ConfigureAwait(false);
        if (!entry.IsSuccess)
        {
            if (entry.Error is not null && entry.Error.Kind == ErrorKind.Cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return new Totals(0, 0);
        }

        return await CountEntryAsync(provider, entry.Value, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Totals> CountEntryAsync(
        IFileSystemProvider provider,
        Entry entry,
        CancellationToken cancellationToken)
    {
        if (entry.IsLink || !entry.IsDirectory)
        {
            var length = entry.IsLink ? 0 : ToLong(entry.Size);
            return new Totals(1, length);
        }

        var files = 0;
        var bytes = 0L;
        await foreach (var child in provider.ListAsync(entry.Path, true, cancellationToken).ConfigureAwait(false))
        {
            if (!child.IsSuccess)
            {
                if (child.Error is not null && child.Error.Kind == ErrorKind.Cancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                continue;
            }

            if (IsParent(child.Value))
            {
                continue;
            }

            var nested = await CountEntryAsync(provider, child.Value, cancellationToken).ConfigureAwait(false);
            files += nested.Files;
            bytes += nested.Bytes;
        }

        return new Totals(files, bytes);
    }

    internal static bool IsParent(Entry entry) =>
        entry.IsParent || entry.Name == "." || entry.Name == "..";

    internal static long ToLong(ulong size) => size > long.MaxValue ? long.MaxValue : (long)size;
}
