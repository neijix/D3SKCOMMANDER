using D3skCommander.Core.Common;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.FileOps;

/// <summary>Creates every missing parent of a path. An existing directory is success.</summary>
internal static class FolderChain
{
    public static async Task<OperationError?> EnsureAsync(
        IFileSystemProvider provider,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (cancellationToken.IsCancellationRequested)
        {
            return OperationError.Cancelled(path);
        }

        var missing = new List<string>();
        var walk = await WalkUpAsync(provider, path, missing).ConfigureAwait(false);
        if (walk is not null)
        {
            return walk;
        }

        return await CreateMissingAsync(provider, missing, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OperationError?> WalkUpAsync(
        IFileSystemProvider provider,
        string path,
        List<string> missing)
    {
        var current = path;
        while (true)
        {
            var entry = await provider.GetEntryAsync(current, CancellationToken.None).ConfigureAwait(false);
            if (entry.IsSuccess)
            {
                return entry.Value.IsDirectory
                    ? null
                    : OperationError.InvalidData("not a folder", current);
            }

            if (entry.Error is not null && entry.Error.Kind != ErrorKind.NotFound)
            {
                return entry.Error;
            }

            missing.Add(current);
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current));
            if (string.IsNullOrEmpty(parent) || parent == current)
            {
                return null;
            }

            current = parent;
        }
    }

    private static async Task<OperationError?> CreateMissingAsync(
        IFileSystemProvider provider,
        List<string> missing,
        CancellationToken cancellationToken)
    {
        for (var i = missing.Count - 1; i >= 0; i--)
        {
            var created = await provider.CreateFolderAsync(missing[i], cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess && created.Error is not null && created.Error.Kind != ErrorKind.AlreadyExists)
            {
                return created.Error;
            }
        }

        return null;
    }
}
